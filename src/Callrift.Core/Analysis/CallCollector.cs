using System.Collections.Concurrent;
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
            result.Add(CreateConversionCall(node, method, conversion.ConstrainedToType));
    }

    private void WalkCore(SyntaxNode node, List<CallStep> result)
    {
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
                    result.Add(CreateConversionCall(cast, conversionMethod, conversion.ConstrainedToType));
                return;
            case ExpressionSyntax expression when expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax
                && model.GetSymbolInfo(expression, cancellationToken).Symbol is IMethodSymbol method
                && (model.GetTypeInfo(expression, cancellationToken).ConvertedType?.TypeKind == TypeKind.Delegate
                    || model.GetOperation(expression, cancellationToken) is IMethodReferenceOperation):
                if (expression is MemberAccessExpressionSyntax methodAccess)
                    Walk(methodAccess.Expression, result);
                Callback(expression, [CreateCall(expression, method, [])], result);
                return;
            case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                Walk(assignment.Left, result);
                Branch("if (" + SymbolNames.Compact(assignment.Left) + " is null)", assignment, [assignment.Right], result);
                return;
            case AssignmentExpressionSyntax assignment when StaticMember(assignment.Left) is { } assignedMember:
                var readBeforeAssignment = !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignedMember is not IEventSymbol;
                if (readBeforeAssignment) Walk(assignment.Left, result);
                Walk(assignment.Right, result);
                if (!readBeforeAssignment) AddStaticMember(assignment.Left, result);
                return;
            case MemberAccessExpressionSyntax access:
                Walk(access.Expression, result);
                AddStaticMember(access, result);
                return;
            case IdentifierNameSyntax identifier:
                AddStaticMember(identifier, result);
                return;
            case InvocationExpressionSyntax invocation:
                if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } && model.GetConstantValue(invocation, cancellationToken).HasValue)
                    return;
                Walk(invocation.Expression, result);
                Emit(invocation, invocation.ArgumentList.Arguments, result);
                return;
            case BaseObjectCreationExpressionSyntax creation:
                Emit(creation, creation.ArgumentList?.Arguments ?? [], result);
                Walk(creation.Initializer, result);
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
                Branch("if (" + SymbolNames.Compact(conditional.Condition) + ")", conditional, [conditional.Statement], result);
                if (conditional.Else is not null)
                    Branch("else (!(" + SymbolNames.Compact(conditional.Condition) + "))", conditional.Else, [conditional.Else.Statement], result);
                return;
            case SwitchStatementSyntax selection:
                Walk(selection.Expression, result);
                foreach (var section in selection.Sections)
                    Branch(string.Join(" ", section.Labels.Select(SymbolNames.Compact)), section,
                        section.Labels.OfType<CasePatternSwitchLabelSyntax>().Select(l => l.WhenClause).OfType<SyntaxNode>().Concat(section.Statements), result);
                return;
            case SwitchExpressionSyntax selection:
                Walk(selection.GoverningExpression, result);
                foreach (var arm in selection.Arms)
                    Branch("case " + SymbolNames.Compact(arm.Pattern) + (arm.WhenClause is null ? "" : " " + SymbolNames.Compact(arm.WhenClause)), arm,
                        arm.WhenClause is null ? [arm.Expression] : [arm.WhenClause, arm.Expression], result);
                return;
            case TryStatementSyntax attempt:
                Branch("try", attempt.Block, [attempt.Block], result);
                foreach (var handler in attempt.Catches)
                    Branch("catch" + (handler.Declaration is null ? "" : " " + SymbolNames.Compact(handler.Declaration))
                        + (handler.Filter is null ? "" : " " + SymbolNames.Compact(handler.Filter)), handler,
                        handler.Filter is null ? [handler.Block] : [handler.Filter, handler.Block], result);
                if (attempt.Finally is not null)
                    Branch("finally", attempt.Finally, [attempt.Finally.Block], result);
                return;
            case ForEachStatementSyntax loop:
                Walk(loop.Expression, result);
                Branch($"foreach ({SymbolNames.Compact(loop.Type)} {loop.Identifier} in {SymbolNames.Compact(loop.Expression)})", loop, [loop.Statement], result);
                return;
            case ForEachVariableStatementSyntax loop:
                Walk(loop.Expression, result);
                Branch($"foreach ({SymbolNames.Compact(loop.Variable)} in {SymbolNames.Compact(loop.Expression)})", loop, [loop.Statement], result);
                return;
            case ForStatementSyntax loop:
                Walk(loop.Declaration, result);
                foreach (var initial in loop.Initializers) Walk(initial, result);
                Branch("for (" + (loop.Condition is null ? "" : SymbolNames.Compact(loop.Condition)) + ")", loop,
                    new SyntaxNode?[] { loop.Condition, loop.Statement }.Where(n => n is not null).Cast<SyntaxNode>().Concat(loop.Incrementors), result);
                return;
            case WhileStatementSyntax loop:
                Branch("while (" + SymbolNames.Compact(loop.Condition) + ")", loop, [loop.Condition, loop.Statement], result);
                return;
            case DoStatementSyntax loop:
                Branch("do / while (" + SymbolNames.Compact(loop.Condition) + ")", loop, [loop.Statement, loop.Condition], result);
                return;
            case ConditionalExpressionSyntax conditional:
                Walk(conditional.Condition, result);
                Branch("if (" + SymbolNames.Compact(conditional.Condition) + ")", conditional.WhenTrue, [conditional.WhenTrue], result);
                Branch("else (!(" + SymbolNames.Compact(conditional.Condition) + "))", conditional.WhenFalse, [conditional.WhenFalse], result);
                return;
            case ConditionalAccessExpressionSyntax access:
                Walk(access.Expression, result);
                Branch("if (" + SymbolNames.Compact(access.Expression) + " is not null)", access, [access.WhenNotNull], result);
                return;
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression) || binary.IsKind(SyntaxKind.CoalesceExpression):
                Walk(binary.Left, result);
                var predicate = binary.Kind() switch
                {
                    SyntaxKind.LogicalAndExpression => SymbolNames.Compact(binary.Left),
                    SyntaxKind.LogicalOrExpression => "!(" + SymbolNames.Compact(binary.Left) + ")",
                    _ => SymbolNames.Compact(binary.Left) + " is null"
                };
                Branch("if (" + predicate + ")", binary, [binary.Right], result);
                return;
        }
        foreach (var child in node.ChildNodes())
            Walk(child, result);
    }

    private ISymbol? StaticMember(SyntaxNode node) => model.GetSymbolInfo(node, cancellationToken).Symbol switch
    {
        IFieldSymbol { IsStatic: true, IsConst: false } field => field,
        IPropertySymbol { IsStatic: true } property => property,
        IEventSymbol { IsStatic: true } eventSymbol => eventSymbol,
        _ => null
    };

    private void AddStaticMember(SyntaxNode node, List<CallStep> result)
    {
        if (StaticMember(node) is not { } member) return;
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

    private void Emit(SyntaxNode invocation, SeparatedSyntaxList<ArgumentSyntax> arguments, List<CallStep> result) =>
        Emit(invocation, arguments.Select(argument => argument.Expression), result);

    private void Emit(SyntaxNode invocation, IEnumerable<ExpressionSyntax> arguments, List<CallStep> result,
        SymbolInfo? initializerBinding = null, ExpressionSyntax? collection = null)
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
        var info = initializerBinding ?? model.GetSymbolInfo(invocation, cancellationToken);
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

    public CallStep ImplicitConstructor(SyntaxNode declaration, IMethodSymbol constructor) => CreateCall(declaration, constructor, []) with
    { Label = symbols.Label(constructor), SuppressDispatch = true, UsesContainingInstance = true };

    private CallStep CreateConversionCall(SyntaxNode node, IMethodSymbol method, ITypeSymbol? constrainedType)
    {
        var call = CreateCall(node, method, []);
        return constrainedType is null ? call : call with
        {
            SuppressDispatch = false,
            DispatchType = DescribeType(method.ContainingType),
            ReceiverType = DescribeType(constrainedType)
        };
    }

    private CallStep CreateCall(SyntaxNode node, IMethodSymbol method, IReadOnlyList<CallStep> children, ExpressionSyntax? collection = null)
    {
        var normalized = SymbolNames.Normalize(method);
        dispatchTypes.Add(method.ContainingType);
        foreach (var argument in method.TypeArguments) dispatchTypes.Add(argument);
        var source = method.MethodKind != MethodKind.DelegateInvoke && normalized.ContainingType.Locations.Any(l => l.IsInSource);
        var expression = node is InvocationExpressionSyntax invocation ? invocation.Expression : node;
        var receiver = collection ?? (expression switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            MemberBindingExpressionSyntax => expression.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression,
            WithExpressionSyntax copy => copy.Expression,
            _ => null
        });
        var exactReceiver = receiver is BaseExpressionSyntax
            || receiver is BaseObjectCreationExpressionSyntax && model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol
            || receiver is not null && model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol { IsSealed: true }
            || receiver is null && model.GetEnclosingSymbol(node.SpanStart, cancellationToken)?.ContainingType is { IsSealed: true };
        var dispatches = !exactReceiver && (method.ContainingType.TypeKind == TypeKind.Interface || method.IsAbstract || method.IsVirtual || method.IsOverride);
        return new CallStep("call", symbols.Key(normalized), source || method.MethodKind == MethodKind.Conversion || collection is not null || node is ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax or WithExpressionSyntax
            ? symbols.Label(normalized) : SymbolNames.SyntaxLabel(node), source, symbols.Location(node), children)
        {
            AlignmentKey = AlignmentKey(node),
            SuppressDispatch = exactReceiver,
            InitializationTriggerType = TypeInitialization.Triggers(method) ? DescribeType(method.ContainingType) : null,
            InitializationScope = TypeInitialization.Triggers(method) ? symbols.InitializationScope(method.ContainingType) : null,
            DispatchType = dispatches ? DescribeType(method.ContainingType) : null,
            ReceiverType = dispatches ? ReceiverConstraint(receiver, node.SpanStart) : null,
            InvocationReceiverType = !GenericBindings.HasContainingInstance(method) ? null : method.MethodKind == MethodKind.Constructor
                ? DescribeType(method.ContainingType) : ReceiverConstraint(receiver, node.SpanStart),
            UsesContainingInstance = GenericBindings.HasContainingInstance(method) && UsesContainingInstance(node, receiver),
            InvocationReceiverExact = GenericBindings.HasContainingInstance(method) && (collection is null && node is BaseObjectCreationExpressionSyntax || ReceiverOperation(receiver) is IObjectCreationOperation),
            GenericArguments = GenericBindings.FromMethod(method),
            MethodArguments = method.TypeArguments.Select(DispatchType.From).ToArray()
        };
    }

    private static string? AlignmentKey(SyntaxNode node) => node is ExpressionSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax
        ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0", node.DescendantTokens().Select(token => token.RawKind + ":" + token.Text)))))
        : null;

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

    private void Branch(string label, SyntaxNode node, IEnumerable<SyntaxNode> bodies, List<CallStep> result)
    {
        var calls = new List<CallStep>();
        foreach (var body in bodies)
            Walk(body, calls);
        if (calls.Count > 0)
            result.Add(new CallStep("branch", "branch:" + label, label.Length > 100 ? label[..97] + "…" : label, true, symbols.Location(node), calls));
    }
}
