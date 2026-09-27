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
                    result.Add(CreateConversionCall(cast, conversionMethod, conversion.ConstrainedToType));
                return;
            case ExpressionSyntax expression when expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax
                && symbol is IMethodSymbol method
                && (model.GetTypeInfo(expression, cancellationToken).ConvertedType?.TypeKind == TypeKind.Delegate
                    || model.GetOperation(expression, cancellationToken) is IMethodReferenceOperation):
                if (expression is MemberAccessExpressionSyntax methodAccess)
                    Walk(methodAccess.Expression, result);
                Callback(expression, [CreateCall(expression, method, [])], result);
                return;
            case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                Walk(assignment.Left, result);
                Branch(assignment, [assignment.Right], result, static node => "if (" + SymbolNames.Compact(node.Left) + " is null)");
                return;
            case AssignmentExpressionSyntax assignment when StaticMember(model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol) is { } assignedMember:
                var readBeforeAssignment = !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignedMember is not IEventSymbol;
                if (readBeforeAssignment) Walk(assignment.Left, result);
                Walk(assignment.Right, result);
                if (!readBeforeAssignment) AddStaticMember(assignment.Left, assignedMember, result);
                return;
            case MemberAccessExpressionSyntax access:
                Walk(access.Expression, result);
                AddStaticMember(access, symbol, result);
                return;
            case IdentifierNameSyntax identifier:
                AddStaticMember(identifier, symbol, result);
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
                Branch(conditional, [conditional.Statement], result, static node => "if (" + SymbolNames.Compact(node.Condition) + ")");
                if (conditional.Else is not null)
                    Branch(conditional, [conditional.Else.Statement], result, static node => "else (!(" + SymbolNames.Compact(node.Condition) + "))", conditional.Else);
                return;
            case SwitchStatementSyntax selection:
                Walk(selection.Expression, result);
                foreach (var section in selection.Sections)
                    Branch(section, section.Labels.OfType<CasePatternSwitchLabelSyntax>().Select(l => l.WhenClause).OfType<SyntaxNode>().Concat(section.Statements), result,
                        static node => string.Join(" ", node.Labels.Select(SymbolNames.Compact)));
                return;
            case SwitchExpressionSyntax selection:
                Walk(selection.GoverningExpression, result);
                foreach (var arm in selection.Arms)
                    Branch(arm, arm.WhenClause is null ? [arm.Expression] : [arm.WhenClause, arm.Expression], result,
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
                Branch(binary, [binary.Right], result, static node => "if (" + (node.Kind() switch
                {
                    SyntaxKind.LogicalAndExpression => SymbolNames.Compact(node.Left),
                    SyntaxKind.LogicalOrExpression => "!(" + SymbolNames.Compact(node.Left) + ")",
                    _ => SymbolNames.Compact(node.Left) + " is null"
                }) + ")");
                return;
        }
        foreach (var child in node.ChildNodesAndTokens())
            if (child.AsNode() is { } childNode) Walk(childNode, result);
    }

    private static ISymbol? StaticMember(ISymbol? symbol) => symbol switch
    {
        IFieldSymbol { IsStatic: true, IsConst: false } field => field,
        IPropertySymbol { IsStatic: true } property => property,
        IEventSymbol { IsStatic: true } eventSymbol => eventSymbol,
        _ => null
    };

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

    public CallStep ImplicitConstructor(SyntaxNode declaration, IMethodSymbol constructor) => CreateCall(declaration, constructor, [], label: symbols.Label(constructor)) with
    { SuppressDispatch = true, UsesContainingInstance = true };

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

    private CallStep CreateCall(SyntaxNode node, IMethodSymbol method, IReadOnlyList<CallStep> children, ExpressionSyntax? collection = null, string? label = null)
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
        return new CallStep("call", symbols.Key(normalized), label ?? (source || method.MethodKind == MethodKind.Conversion || collection is not null || node is ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax or WithExpressionSyntax
            ? symbols.Label(normalized) : SymbolNames.SyntaxLabel(node)), source, symbols.Location(node), children)
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

    private static string? AlignmentKey(SyntaxNode node)
    {
        if (node is not (ExpressionSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax)) return null;
        var text = new DefaultInterpolatedStringHandler(0, 0, null, stackalloc char[256]);
        try
        {
            var first = true;
            foreach (var token in node.DescendantTokens())
            {
                if (!first) text.AppendLiteral("\0");
                first = false;
                text.AppendFormatted(token.RawKind);
                text.AppendLiteral(":");
                text.AppendLiteral(token.Text);
            }
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
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
        var label = formatLabel(node);
        result.Add(new CallStep("branch", "branch:" + label, label.Length > 100 ? label[..97] + "…" : label, true, symbols.Location(location ?? node), calls));
    }
}
