using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Callrift.Core;

internal sealed class CallCollector(SemanticModel model, SymbolNames symbols, ConcurrentBag<AnalysisDiagnostic> diagnostics,
    ConcurrentBag<ITypeSymbol> dispatchTypes, CancellationToken cancellationToken)
{
    private ConditionalOperatorBindings? conditionalOperators;

    public IReadOnlyList<CallStep> Collect(SyntaxNode node)
    {
        var result = new List<CallStep>();
        Walk(node, result);
        return result;
    }

    private void Walk(SyntaxNode? node, List<CallStep> result)
    {
        if (node is null)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        WalkCore(node, result);
        if (node is ExpressionSyntax and not ParenthesizedExpressionSyntax && model.GetConversion(node, cancellationToken) is { IsUserDefined: true, MethodSymbol: { } method } conversion
            && model.GetOperation(node, cancellationToken) is not null)
            result.Add(CreateOperatorCall(node, method, conversion.ConstrainedToType));
        if (node is ExpressionSyntax condition && IsCondition(condition) && model.GetOperation(condition, cancellationToken) is { } operation)
        {
            while (operation.Parent is IConversionOperation { IsImplicit: true } parent) operation = parent;
            if (operation.Parent is IUnaryOperation { IsImplicit: true, OperatorMethod: { } truth } unary)
                result.Add(CreateOperatorCall(condition, truth, unary.ConstrainedToType));
        }
    }

    private void WalkCore(SyntaxNode node, List<CallStep> result)
    {
        var symbol = node is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax
            ? model.GetSymbolInfo(node, cancellationToken).Symbol : null;
        switch (node)
        {
            case LocalFunctionStatementSyntax or BaseTypeDeclarationSyntax or MethodDeclarationSyntax:
                return;
            case AnonymousFunctionExpressionSyntax lambda:
                var body = new List<CallStep>();
                Walk(lambda.Body, body);
                Callback(lambda, body, result);
                return;
            case CastExpressionSyntax cast:
                Walk(cast.Expression, result);
                if (model.GetOperation(cast, cancellationToken) is IConversionOperation { OperatorMethod: { } conversionMethod } conversion)
                    result.Add(CreateOperatorCall(cast, conversionMethod, conversion.ConstrainedToType));
                return;
            case ExpressionSyntax expression when expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax
                && symbol is IMethodSymbol method
                && (model.GetTypeInfo(expression, cancellationToken).ConvertedType?.TypeKind == TypeKind.Delegate
                    || model.GetOperation(expression, cancellationToken) is IMethodReferenceOperation):
                if (expression is MemberAccessExpressionSyntax methodAccess)
                    Walk(methodAccess.Expression, result);
                Callback(expression, [CreateCall(expression, method, [])], result);
                return;
            case AssignmentExpressionSyntax assignment when assignment.Kind() is SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression
                && model.GetOperation(assignment, cancellationToken) is IEventAssignmentOperation
                { EventReference: IEventReferenceOperation eventReference } eventAssignment
                && (eventAssignment.Adds ? eventReference.Event.AddMethod : eventReference.Event.RemoveMethod) is { } accessor:
                if (assignment.Left is MemberAccessExpressionSyntax eventAccess) Walk(eventAccess.Expression, result);
                Walk(assignment.Right, result);
                if (accessor is { IsImplicitlyDeclared: true, IsAbstract: false }) AddStaticMember(assignment.Left, result);
                result.Add(CreateCall(assignment.Left, accessor, []));
                return;
            case AssignmentExpressionSyntax assignment when model.GetOperation(assignment, cancellationToken) is IDeconstructionAssignmentOperation:
                var targets = AssignmentTargets(assignment.Left).ToArray();
                foreach (var target in targets)
                    if (model.GetOperation(target, cancellationToken) is IPropertyReferenceOperation { Property.ReturnsByRef: true }) Walk(target, result);
                    else WalkPropertyInputs(target, result);
                Walk(assignment.Right, result);
                foreach (var target in targets)
                {
                    AddPropertySetter(target, result);
                    AddStaticMember(target, result);
                }
                return;
            case AssignmentExpressionSyntax assignment when assignment.Right is InitializerExpressionSyntax nested
                && model.GetOperation(assignment, cancellationToken) is IMemberInitializerOperation:
                WalkMemberInitializer(assignment, nested, [], result);
                return;
            case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                Walk(assignment.Left, result);
                var coalesced = new List<CallStep>();
                Walk(assignment.Right, coalesced);
                AddPropertySetter(assignment.Left, coalesced);
                if (coalesced.Count > 0) AddBranch("if (" + SymbolNames.Compact(assignment.Left) + " is null)", assignment, coalesced, result);
                return;
            case AssignmentExpressionSyntax assignment when !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && model.GetOperation(assignment, cancellationToken) is ICompoundAssignmentOperation compound:
                Walk(assignment.Left, result);
                if (compound.InConversion is { IsUserDefined: true, MethodSymbol: { } input })
                    result.Add(CreateOperatorCall(assignment.Left, input, compound.InConversion.ConstrainedToType));
                Walk(assignment.Right, result);
                if (compound.OperatorMethod is { } assignmentOperator)
                    result.Add(CreateOperatorCall(assignment, assignmentOperator, compound.ConstrainedToType, assignment.Left));
                if (compound.OutConversion is { IsUserDefined: true, MethodSymbol: { } output })
                    result.Add(CreateOperatorCall(assignment, output, compound.OutConversion.ConstrainedToType));
                AddPropertySetter(assignment.Left, result);
                return;
            case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && PropertyReference(assignment.Left) is { } target:
                if (target.Property.ReturnsByRef || target.Property.ReturnsByRefReadonly) Walk(assignment.Left, result);
                else WalkPropertyInputs(assignment.Left, result);
                Walk(assignment.Right, result);
                AddPropertySetter(assignment.Left, result);
                return;
            case AssignmentExpressionSyntax assignment when StaticMember(model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol) is { } assignedMember:
                var readBeforeAssignment = !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignedMember is not IEventSymbol;
                if (readBeforeAssignment) Walk(assignment.Left, result);
                Walk(assignment.Right, result);
                if (!readBeforeAssignment) AddStaticMember(assignment.Left, assignedMember, result);
                return;
            case ExpressionSyntax expression when expression is IdentifierNameSyntax or MemberAccessExpressionSyntax or MemberBindingExpressionSyntax
                or ElementAccessExpressionSyntax or ElementBindingExpressionSyntax or ImplicitElementAccessSyntax
                && model.GetOperation(expression, cancellationToken) is IPropertyReferenceOperation property:
                WalkPropertyInputs(expression, result);
                if (property.Property.GetMethod is { } getter)
                    result.Add(CreateCall(expression, getter, []));
                return;
            case MemberAccessExpressionSyntax access:
                Walk(access.Expression, result);
                AddStaticMember(access, symbol, result);
                return;
            case IdentifierNameSyntax identifier:
                AddStaticMember(identifier, symbol, result);
                return;
            case FieldExpressionSyntax field:
                AddStaticMember(field, result);
                return;
            case InvocationExpressionSyntax invocation:
                if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } && model.GetConstantValue(invocation, cancellationToken).HasValue)
                    return;
                var invocationBinding = model.GetSymbolInfo(invocation, cancellationToken);
                if (invocationBinding.Symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.ReducedExtension or MethodKind.LocalFunction }
                    && invocation.Expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax)
                {
                    if (invocation.Expression is MemberAccessExpressionSyntax invokedMember) Walk(invokedMember.Expression, result);
                }
                else
                    Walk(invocation.Expression, result);
                Emit(invocation, invocation.ArgumentList.Arguments, result, invocationBinding);
                return;
            case BaseObjectCreationExpressionSyntax creation:
                Emit(creation, creation.ArgumentList?.Arguments ?? [], result);
                Walk(creation.Initializer, result);
                return;
            case AnonymousObjectMemberDeclaratorSyntax member:
                Walk(member.Expression, result);
                return;
            case InitializerExpressionSyntax initializer when initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                var collection = initializer.Parent switch
                {
                    BaseObjectCreationExpressionSyntax owner => owner,
                    AssignmentExpressionSyntax assignment => assignment.Left,
                    _ => null
                };
                foreach (var element in initializer.Expressions)
                    Emit(element, element is InitializerExpressionSyntax values ? values.Expressions : [element], result,
                        model.GetCollectionInitializerSymbolInfo(element, cancellationToken), collection);
                return;
            case ConstructorInitializerSyntax initializer:
                Emit(initializer, initializer.ArgumentList.Arguments, result);
                return;
            case PrimaryConstructorBaseTypeSyntax primaryBase:
                Emit(primaryBase, primaryBase.ArgumentList.Arguments, result);
                return;
            case WithExpressionSyntax copy:
                Walk(copy.Expression, result);
                if (model.GetOperation(copy, cancellationToken) is IWithOperation { CloneMethod: { } clone })
                    result.Add(CreateCall(copy, clone, []));
                Walk(copy.Initializer, result);
                return;
            case IfStatementSyntax conditional:
                Walk(conditional.Condition, result);
                Branch(conditional, [conditional.Statement], result, static node => "if (" + SymbolNames.Compact(node.Condition) + ")");
                if (conditional.Else is not null)
                    Branch(conditional, [conditional.Else.Statement], result, static node => "else (!(" + SymbolNames.Compact(node.Condition) + "))", conditional.Else);
                return;
            case IsPatternExpressionSyntax pattern:
                Walk(pattern.Expression, result);
                Branch(pattern, [pattern.Pattern], result, static node => "pattern (" + SymbolNames.Compact(node.Expression) + " is " + SymbolNames.Compact(node.Pattern) + ")");
                return;
            case SwitchStatementSyntax selection:
                Walk(selection.Expression, result);
                foreach (var section in selection.Sections)
                    Branch(section, section.Labels.OfType<CasePatternSwitchLabelSyntax>().SelectMany(label => label.WhenClause is null
                        ? new SyntaxNode[] { label.Pattern } : [label.Pattern, label.WhenClause]).Concat(section.Statements), result,
                        static node => string.Join(" ", node.Labels.Select(SymbolNames.Compact)));
                return;
            case SwitchExpressionSyntax selection:
                Walk(selection.GoverningExpression, result);
                foreach (var arm in selection.Arms)
                    Branch(arm, arm.WhenClause is null ? [arm.Pattern, arm.Expression] : [arm.Pattern, arm.WhenClause, arm.Expression], result,
                        static node => "case " + SymbolNames.Compact(node.Pattern) + (node.WhenClause is null ? "" : " " + SymbolNames.Compact(node.WhenClause)));
                return;
            case TryStatementSyntax attempt:
                Branch(attempt.Block, [attempt.Block], result, static _ => "try");
                foreach (var handler in attempt.Catches)
                    Branch(handler, handler.Filter is null ? [handler.Block] : [handler.Filter, handler.Block], result,
                        static node => "catch" + (node.Declaration is null ? "" : " " + SymbolNames.Compact(node.Declaration))
                            + (node.Filter is null ? "" : " " + SymbolNames.Compact(node.Filter)));
                if (attempt.Finally is not null)
                    Branch(attempt.Finally, [attempt.Finally.Block], result, static _ => "finally");
                return;
            case ForEachStatementSyntax loop:
                Walk(loop.Expression, result);
                Branch(loop, [loop.Statement], result, static node => $"foreach ({SymbolNames.Compact(node.Type)} {node.Identifier} in {SymbolNames.Compact(node.Expression)})");
                return;
            case ForEachVariableStatementSyntax loop:
                Walk(loop.Expression, result);
                Branch(loop, [loop.Statement], result, static node => $"foreach ({SymbolNames.Compact(node.Variable)} in {SymbolNames.Compact(node.Expression)})");
                return;
            case ForStatementSyntax loop:
                Walk(loop.Declaration, result);
                foreach (var initial in loop.Initializers) Walk(initial, result);
                Branch(loop, new SyntaxNode?[] { loop.Condition, loop.Statement }.Where(n => n is not null).Cast<SyntaxNode>().Concat(loop.Incrementors), result,
                    static node => "for (" + (node.Condition is null ? "" : SymbolNames.Compact(node.Condition)) + ")");
                return;
            case WhileStatementSyntax loop:
                Branch(loop, [loop.Condition, loop.Statement], result, static node => "while (" + SymbolNames.Compact(node.Condition) + ")");
                return;
            case DoStatementSyntax loop:
                Branch(loop, [loop.Statement, loop.Condition], result, static node => "do / while (" + SymbolNames.Compact(node.Condition) + ")");
                return;
            case ConditionalExpressionSyntax conditional:
                Walk(conditional.Condition, result);
                Branch(conditional, [conditional.WhenTrue], result, static node => "if (" + SymbolNames.Compact(node.Condition) + ")", conditional.WhenTrue);
                Branch(conditional, [conditional.WhenFalse], result, static node => "else (!(" + SymbolNames.Compact(node.Condition) + "))", conditional.WhenFalse);
                return;
            case ConditionalAccessExpressionSyntax access:
                Walk(access.Expression, result);
                Branch(access, [access.WhenNotNull], result, static node => "if (" + SymbolNames.Compact(node.Expression) + " is not null)");
                return;
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression) || binary.IsKind(SyntaxKind.CoalesceExpression):
                Walk(binary.Left, result);
                if (model.GetOperation(binary, cancellationToken) is IBinaryOperation { OperatorMethod: { } binaryOperator } logical)
                {
                    var truth = (conditionalOperators ??= new ConditionalOperatorBindings(cancellationToken)).Find(logical);
                    if (truth?.OperatorMethod is { } truthOperator)
                        result.Add(CreateOperatorCall(binary.Left, truthOperator, truth.ConstrainedToType));
                    else
                        diagnostics.Add(new AnalysisDiagnostic("unresolved-operator", "Cannot bind short-circuit truth operator for " + SymbolNames.Compact(binary), symbols.Location(binary)));
                    var right = new List<CallStep>();
                    Walk(binary.Right, right);
                    right.Add(CreateOperatorCall(binary, binaryOperator, logical.ConstrainedToType));
                    AddBranch("if (!" + (truth?.OperatorMethod is { } boundTruth ? symbols.Label(boundTruth) : "truth operator") + "(" + SymbolNames.Compact(binary.Left) + "))", binary, right, result);
                    return;
                }
                Branch(binary, [binary.Right], result, static node => "if (" + (node.Kind() switch
                {
                    SyntaxKind.LogicalAndExpression => SymbolNames.Compact(node.Left),
                    SyntaxKind.LogicalOrExpression => "!(" + SymbolNames.Compact(node.Left) + ")",
                    _ => SymbolNames.Compact(node.Left) + " is null"
                }) + ")");
                return;
            case BinaryExpressionSyntax binary:
                Walk(binary.Left, result);
                Walk(binary.Right, result);
                if (model.GetOperation(binary, cancellationToken) is IBinaryOperation { OperatorMethod: { } binaryMethod } operation)
                    result.Add(CreateOperatorCall(binary, binaryMethod, operation.ConstrainedToType));
                return;
            case PrefixUnaryExpressionSyntax prefix:
                Walk(prefix.Operand, result);
                AddUnaryOperator(prefix, prefix.Operand, result);
                if (prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression) AddPropertySetter(prefix.Operand, result);
                return;
            case PostfixUnaryExpressionSyntax postfix:
                Walk(postfix.Operand, result);
                AddUnaryOperator(postfix, postfix.Operand, result);
                if (postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression) AddPropertySetter(postfix.Operand, result);
                return;
        }
        foreach (var child in node.ChildNodesAndTokens())
            if (child.AsNode() is { } childNode) Walk(childNode, result);
    }

    private void WalkPropertyInputs(ExpressionSyntax expression, List<CallStep> result)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                WalkPropertyInputs(parenthesized.Expression, result);
                break;
            case PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                WalkPropertyInputs(suppression.Operand, result);
                break;
            case MemberAccessExpressionSyntax access:
                Walk(access.Expression, result);
                break;
            case ElementAccessExpressionSyntax access:
                Walk(access.Expression, result);
                foreach (var argument in access.ArgumentList.Arguments) Walk(argument.Expression, result);
                break;
            case ElementBindingExpressionSyntax binding:
                foreach (var argument in binding.ArgumentList.Arguments) Walk(argument.Expression, result);
                break;
            case ImplicitElementAccessSyntax access:
                foreach (var argument in access.ArgumentList.Arguments) Walk(argument.Expression, result);
                break;
        }
    }

    private void WalkMemberInitializer(AssignmentExpressionSyntax assignment, InitializerExpressionSyntax initializer,
        IReadOnlyList<CallStep> receivers, List<CallStep> result)
    {
        WalkPropertyInputs(assignment.Left, result);
        var accesses = receivers.ToList();
        if (model.GetOperation(assignment.Left, cancellationToken) is IPropertyReferenceOperation { Property.GetMethod: { } getter })
            accesses.Add(CreateCall(assignment.Left, getter, []));
        else AddStaticMember(assignment.Left, accesses);
        foreach (var element in initializer.Expressions)
        {
            if (element is AssignmentExpressionSyntax { Right: InitializerExpressionSyntax nested } member
                && model.GetOperation(member, cancellationToken) is IMemberInitializerOperation)
            {
                WalkMemberInitializer(member, nested, accesses, result);
                continue;
            }
            result.AddRange(accesses);
            if (initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
                Emit(element, element is InitializerExpressionSyntax values ? values.Expressions : [element], result,
                    model.GetCollectionInitializerSymbolInfo(element, cancellationToken), assignment.Left);
            else Walk(element, result);
        }
    }

    private static IEnumerable<ExpressionSyntax> AssignmentTargets(ExpressionSyntax expression) => expression switch
    {
        TupleExpressionSyntax tuple => tuple.Arguments.SelectMany(argument => AssignmentTargets(argument.Expression)),
        ParenthesizedExpressionSyntax parenthesized => AssignmentTargets(parenthesized.Expression),
        _ => [expression]
    };

    private void AddPropertySetter(ExpressionSyntax expression, List<CallStep> result)
    {
        if (PropertyReference(expression) is
            { Property: { ReturnsByRef: false, ReturnsByRefReadonly: false, SetMethod: { } setter } })
            result.Add(CreateCall(expression, setter, []));
    }

    private IPropertyReferenceOperation? PropertyReference(ExpressionSyntax expression) =>
        model.GetOperation(UnwrapProperty(expression), cancellationToken) as IPropertyReferenceOperation;

    private static ExpressionSyntax UnwrapProperty(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax || expression is PostfixUnaryExpressionSyntax suppression && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            expression = expression is ParenthesizedExpressionSyntax parenthesized ? parenthesized.Expression : ((PostfixUnaryExpressionSyntax)expression).Operand;
        return expression;
    }

    private void AddUnaryOperator(ExpressionSyntax expression, ExpressionSyntax operand, List<CallStep> result)
    {
        switch (model.GetOperation(expression, cancellationToken))
        {
            case IUnaryOperation { OperatorMethod: { } method } unary:
                result.Add(CreateOperatorCall(expression, method, unary.ConstrainedToType));
                break;
            case IIncrementOrDecrementOperation { OperatorMethod: { } method } increment:
                result.Add(CreateOperatorCall(expression, method, increment.ConstrainedToType, operand));
                break;
        }
    }

    private static bool IsCondition(ExpressionSyntax expression)
    {
        SyntaxNode node = expression;
        while (node.Parent is ParenthesizedExpressionSyntax or CheckedExpressionSyntax
            || node.Parent is PostfixUnaryExpressionSyntax suppression && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression)) node = node.Parent;
        return node.Parent switch
        {
            IfStatementSyntax statement => statement.Condition == node,
            WhileStatementSyntax statement => statement.Condition == node,
            DoStatementSyntax statement => statement.Condition == node,
            ForStatementSyntax statement => statement.Condition == node,
            ConditionalExpressionSyntax conditional => conditional.Condition == node,
            WhenClauseSyntax clause => clause.Condition == node,
            _ => false
        };
    }

    private static ISymbol? StaticMember(ISymbol? symbol) => symbol switch
    {
        IFieldSymbol { IsStatic: true, IsConst: false } field => field,
        IEventSymbol { IsStatic: true } eventSymbol => eventSymbol,
        _ => null
    };

    private void AddStaticMember(SyntaxNode node, List<CallStep> result) =>
        AddStaticMember(node, model.GetSymbolInfo(node, cancellationToken).Symbol, result);

    private void AddStaticMember(SyntaxNode node, ISymbol? symbol, List<CallStep> result)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (StaticMember(symbol) is not { } member) return;
        if (member.ContainingType.Locations.Any(location => location.IsInSource) && member.ContainingType.StaticConstructors.Length == 0) return;
        var type = DescribeType(member.ContainingType);
        result.Add(new CallStep("initialize", "initialize:" + type.Name, "initialization", true, symbols.Location(node), [])
        { InitializationTriggerType = type, InitializationTriggerIsField = true, InitializationScope = symbols.InitializationScope(member.ContainingType) });
    }

    private void Callback(SyntaxNode node, IReadOnlyList<CallStep> calls, List<CallStep> result)
    {
        if (calls.Count > 0)
            result.Add(new CallStep("branch", "branch:callback", "callback", true, symbols.Location(node), calls)
            { Relation = "callback" });
    }

    private void Emit(SyntaxNode invocation, SeparatedSyntaxList<ArgumentSyntax> arguments, List<CallStep> result, SymbolInfo? invocationBinding = null) =>
        Emit(invocation, arguments.Select(argument => argument.Expression), result, invocationBinding: invocationBinding);

    private void Emit(SyntaxNode invocation, IEnumerable<ExpressionSyntax> arguments, List<CallStep> result,
        SymbolInfo? initializerBinding = null, ExpressionSyntax? collection = null, SymbolInfo? invocationBinding = null)
    {
        var callbacks = new List<CallStep>();
        var argumentIndex = 0;
        foreach (var argument in arguments)
        {
            var callbackStart = callbacks.Count;
            var expression = argument;
            while (expression is ParenthesizedExpressionSyntax || expression is CastExpressionSyntax
                && !model.GetConversion(expression, cancellationToken).IsUserDefined
                && model.GetOperation(expression, cancellationToken) is not IConversionOperation { OperatorMethod: not null })
                expression = expression is ParenthesizedExpressionSyntax parenthesized ? parenthesized.Expression : ((CastExpressionSyntax)expression).Expression;
            if (expression is AnonymousFunctionExpressionSyntax lambda)
                Walk(lambda.Body, callbacks);
            else if (expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax && model.GetSymbolInfo(expression, cancellationToken).Symbol is IMethodSymbol method)
            {
                if (expression is MemberAccessExpressionSyntax access)
                    Walk(access.Expression, result);
                callbacks.Add(CreateCall(expression, method, []));
            }
            else
                Walk(argument, result);
            for (var index = callbackStart; index < callbacks.Count; index++)
                callbacks[index] = callbacks[index] with { CallbackGroup = argumentIndex };
            argumentIndex++;
        }
        var info = initializerBinding ?? invocationBinding ?? model.GetSymbolInfo(invocation, cancellationToken);
        var target = initializerBinding is null && invocation is InvocationExpressionSyntax interceptable
            ? InterceptorSymbols.Find(model, interceptable, cancellationToken) ?? info.Symbol as IMethodSymbol
            : info.Symbol as IMethodSymbol;
        if (target is null && initializerBinding is null && invocation is BaseObjectCreationExpressionSyntax && model.GetOperation(invocation, cancellationToken) is IDelegateCreationOperation
            { Type: INamedTypeSymbol delegateType })
            target = delegateType.InstanceConstructors.SingleOrDefault();
        if (target is not null)
            result.Add(CreateCall(invocation, target, callbacks.Select(c => c with { Relation = "callback" }).ToArray(), collection));
        else
        {
            var label = initializerBinding is null ? SymbolNames.SyntaxLabel(invocation) : "collection initializer Add";
            if (initializerBinding is null && invocation is BaseObjectCreationExpressionSyntax && model.GetTypeInfo(invocation, cancellationToken).Type is ITypeParameterSymbol)
            {
                result.Add(new CallStep("call", "external:" + label, label, false, symbols.Location(invocation), callbacks)
                { AlignmentKey = AlignmentKey(invocation) });
                return;
            }
            var candidates = info.CandidateSymbols.OfType<IMethodSymbol>().Select(symbols.Key).Order(StringComparer.Ordinal).ToArray();
            diagnostics.Add(new AnalysisDiagnostic("unresolved-call", $"Cannot bind {label}" + (candidates.Length == 0 ? "." : "; candidates: " + string.Join(", ", candidates)), symbols.Location(invocation)));
            result.Add(new CallStep("unresolved", "?" + label + string.Join("|", candidates), "? " + label, false, symbols.Location(invocation),
                callbacks.Select(c => c with { Relation = "callback" }).ToArray())
            { Candidates = candidates, AlignmentKey = AlignmentKey(invocation) });
        }
    }

    public CallStep ImplicitConstructor(SyntaxNode declaration, IMethodSymbol constructor) => CreateCall(declaration, constructor, [], label: symbols.Label(constructor)) with
    { SuppressDispatch = true, UsesContainingInstance = true };

    private CallStep CreateOperatorCall(SyntaxNode node, IMethodSymbol method, ITypeSymbol? constrainedType, ExpressionSyntax? receiver = null)
    {
        var call = CreateCall(node, method, [], method.IsStatic ? null : receiver);
        return constrainedType is null ? call : call with
        {
            SuppressDispatch = false,
            DispatchType = DescribeType(method.ContainingType),
            ReceiverType = DescribeType(constrainedType)
        };
    }

    private CallStep CreateCall(SyntaxNode node, IMethodSymbol method, IReadOnlyList<CallStep> children, ExpressionSyntax? collection = null, string? label = null)
    {
        var normalized = SymbolNames.Normalize(method);
        dispatchTypes.Add(method.ContainingType);
        foreach (var argument in method.TypeArguments) dispatchTypes.Add(argument);
        var source = method.MethodKind != MethodKind.DelegateInvoke && normalized.ContainingType.Locations.Any(l => l.IsInSource);
        var expression = node is InvocationExpressionSyntax invocation ? invocation.Expression : node;
        if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet && expression is ExpressionSyntax propertyExpression)
            expression = UnwrapProperty(propertyExpression);
        var receiver = collection ?? (expression switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            ElementAccessExpressionSyntax access => access.Expression,
            MemberBindingExpressionSyntax or ElementBindingExpressionSyntax => expression.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression,
            WithExpressionSyntax copy => copy.Expression,
            _ => null
        });
        if (receiver is null && method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
            && expression.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax initialized } })
            receiver = initialized;
        var implicitPropertyReceiver = receiver is null && method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
            && model.GetOperation(expression, cancellationToken) is IPropertyReferenceOperation { Instance: { IsImplicit: true } instance }
            ? instance : null;
        var exactReceiver = receiver is BaseExpressionSyntax
            || receiver is BaseObjectCreationExpressionSyntax && model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol
            || receiver is not null && model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol { IsSealed: true }
            || implicitPropertyReceiver?.Type is INamedTypeSymbol { IsSealed: true }
            || receiver is null && implicitPropertyReceiver is null && model.GetEnclosingSymbol(node.SpanStart, cancellationToken)?.ContainingType is { IsSealed: true };
        var dispatches = !exactReceiver && (method.ContainingType.TypeKind == TypeKind.Interface || method.IsAbstract || method.IsVirtual || method.IsOverride);
        var receiverType = dispatches || GenericBindings.HasContainingInstance(method)
            ? implicitPropertyReceiver?.Type is { } propertyReceiverType ? DescribeType(propertyReceiverType) : ReceiverConstraint(receiver, node.SpanStart)
            : null;
        return new CallStep("call", symbols.Key(normalized), label ?? (source || method.MethodKind is MethodKind.Conversion or MethodKind.UserDefinedOperator or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.PropertyGet or MethodKind.PropertySet || collection is not null || node is ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax or WithExpressionSyntax
            ? symbols.Label(normalized) : SymbolNames.SyntaxLabel(node)), source, symbols.Location(node), children)
        {
            AlignmentKey = AlignmentKey(node),
            SuppressDispatch = exactReceiver,
            InitializationTriggerType = TypeInitialization.Triggers(method) ? DescribeType(method.ContainingType) : null,
            InitializationScope = TypeInitialization.Triggers(method) ? symbols.InitializationScope(method.ContainingType) : null,
            DispatchType = dispatches ? DescribeType(method.ContainingType) : null,
            ReceiverType = dispatches ? receiverType : null,
            InvocationReceiverType = !GenericBindings.HasContainingInstance(method) ? null : method.MethodKind == MethodKind.Constructor
                ? DescribeType(method.ContainingType) : receiverType,
            UsesContainingInstance = GenericBindings.HasContainingInstance(method) && (implicitPropertyReceiver is null
                ? UsesContainingInstance(node, receiver) : implicitPropertyReceiver is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }),
            InvocationReceiverExact = GenericBindings.HasContainingInstance(method) && (collection is null && node is BaseObjectCreationExpressionSyntax || ReceiverOperation(receiver) is IObjectCreationOperation),
            GenericArguments = GenericBindings.FromMethod(method),
            MethodArguments = method.TypeArguments.Select(DispatchType.From).ToArray()
        };
    }

    private static string? AlignmentKey(SyntaxNode node)
    {
        if (node is not (ExpressionSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax)) return null;
        var text = new DefaultInterpolatedStringHandler(0, 0, null, stackalloc char[256]);
        try
        {
            var first = true;
            var lastToken = node.GetLastToken(includeZeroWidth: true);
            for (var token = node.GetFirstToken(includeZeroWidth: true); token.RawKind != 0; token = token.GetNextToken(includeZeroWidth: true))
            {
                if (!first) text.AppendLiteral("\0");
                first = false;
                text.AppendFormatted(token.RawKind);
                text.AppendLiteral(":");
                text.AppendLiteral(token.Text);
                if (token == lastToken) break;
            }
            var value = text.ToString();
            var byteCount = Encoding.UTF8.GetByteCount(value);
            Span<byte> bytes = byteCount <= 512 ? stackalloc byte[byteCount] : new byte[byteCount];
            Encoding.UTF8.GetBytes(value, bytes);
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(bytes, hash);
            return Convert.ToHexStringLower(hash);
        }
        finally
        {
            text.Clear();
        }
    }

    private DispatchType? ReceiverConstraint(ExpressionSyntax? receiver, int position)
    {
        if (receiver is null)
            return model.GetEnclosingSymbol(position, cancellationToken)?.ContainingType is { TypeKind: TypeKind.Class or TypeKind.Struct } owner
                ? DescribeType(owner) : null;
        ITypeSymbol? constraint = null;
        while (receiver is not null)
        {
            if (receiver is ParenthesizedExpressionSyntax parenthesized)
            {
                receiver = parenthesized.Expression;
                continue;
            }
            var type = model.GetTypeInfo(receiver, cancellationToken).Type;
            if (type is { TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Array or TypeKind.Delegate or TypeKind.Interface or TypeKind.TypeParameter }
                && (constraint is null
                    || constraint.TypeKind == TypeKind.Interface && type.TypeKind != TypeKind.Interface && type.SpecialType != SpecialType.System_Object
                    || model.Compilation.ClassifyCommonConversion(type, constraint).IsImplicit))
                constraint = type;
            if (model.GetOperation(receiver, cancellationToken) is not IConversionOperation conversion
                || !(conversion.Conversion.IsReference || conversion.Conversion.IsIdentity)) break;
            receiver = conversion.Operand.Syntax as ExpressionSyntax;
        }
        return constraint is null ? null : DescribeType(constraint);
    }

    private bool UsesContainingInstance(SyntaxNode node, ExpressionSyntax? receiver)
    {
        if (node is ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax) return true;
        if (node is BaseObjectCreationExpressionSyntax) return false;
        if (receiver is null) return true;
        return ReceiverOperation(receiver) is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance };
    }

    private IOperation? ReceiverOperation(ExpressionSyntax? receiver)
    {
        while (receiver is ParenthesizedExpressionSyntax parenthesized) receiver = parenthesized.Expression;
        var operation = receiver is null ? null : model.GetOperation(receiver, cancellationToken);
        while (operation is IConversionOperation conversion && (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference))
            operation = conversion.Operand;
        return operation;
    }

    private DispatchType DescribeType(ITypeSymbol type)
    {
        dispatchTypes.Add(type);
        return DispatchType.From(type);
    }

    private void Branch<TNode>(TNode node, IEnumerable<SyntaxNode> bodies, List<CallStep> result, Func<TNode, string> formatLabel, SyntaxNode? location = null) where TNode : SyntaxNode
    {
        var calls = new List<CallStep>();
        foreach (var body in bodies)
            Walk(body, calls);
        if (calls.Count == 0) return;
        AddBranch(formatLabel(node), location ?? node, calls, result);
    }

    private void AddBranch(string label, SyntaxNode node, IReadOnlyList<CallStep> calls, List<CallStep> result)
    {
        if (calls.Count > 0)
            result.Add(new CallStep("branch", "branch:" + label, label.Length > 100 ? label[..97] + "…" : label, true, symbols.Location(node), calls));
    }
}
