using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Callrift.Core;

internal sealed class CallCollector(SemanticModel model, SymbolNames symbols, ConcurrentBag<AnalysisDiagnostic> diagnostics, CancellationToken cancellationToken)
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
        switch (node)
        {
            case LocalFunctionStatementSyntax or BaseTypeDeclarationSyntax or MethodDeclarationSyntax or AnonymousFunctionExpressionSyntax:
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
            case ConstructorInitializerSyntax initializer:
                Emit(initializer, initializer.ArgumentList.Arguments, result);
                return;
            case PrimaryConstructorBaseTypeSyntax primaryBase:
                Emit(primaryBase, primaryBase.ArgumentList.Arguments, result);
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
                Branch($"foreach ({loop.Type} {loop.Identifier} in {SymbolNames.Compact(loop.Expression)})", loop, [loop.Statement], result);
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

    private void Emit(SyntaxNode invocation, SeparatedSyntaxList<ArgumentSyntax> arguments, List<CallStep> result)
    {
        var callbacks = new List<CallStep>();
        foreach (var argument in arguments)
        {
            var expression = argument.Expression;
            while (expression is ParenthesizedExpressionSyntax or CastExpressionSyntax)
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
                Walk(expression, result);
        }
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        if (info.Symbol is IMethodSymbol target)
            result.Add(CreateCall(invocation, target, callbacks.Select(c => c with { Relation = "callback" }).ToArray()));
        else
        {
            var label = SymbolNames.SyntaxLabel(invocation);
            if (invocation is BaseObjectCreationExpressionSyntax && model.GetTypeInfo(invocation, cancellationToken).Type is ITypeParameterSymbol)
            {
                result.Add(new CallStep("call", "external:" + label, label, false, symbols.Location(invocation), callbacks));
                return;
            }
            var candidates = info.CandidateSymbols.OfType<IMethodSymbol>().Select(symbols.Key).Order(StringComparer.Ordinal).ToArray();
            diagnostics.Add(new AnalysisDiagnostic("unresolved-call", $"Cannot bind {label}" + (candidates.Length == 0 ? "." : "; candidates: " + string.Join(", ", candidates)), symbols.Location(invocation)));
            result.Add(new CallStep("unresolved", "?" + label + string.Join("|", candidates), "? " + label, false, symbols.Location(invocation),
                callbacks.Select(c => c with { Relation = "callback" }).ToArray())
            { Candidates = candidates });
        }
    }

    private CallStep CreateCall(SyntaxNode node, IMethodSymbol method, IReadOnlyList<CallStep> children)
    {
        var normalized = SymbolNames.Normalize(method);
        var source = method.MethodKind != MethodKind.DelegateInvoke && normalized.ContainingType.Locations.Any(l => l.IsInSource);
        var expression = node is InvocationExpressionSyntax invocation ? invocation.Expression : node;
        var receiver = expression switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            MemberBindingExpressionSyntax => expression.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression,
            _ => null
        };
        var exactReceiver = receiver is BaseExpressionSyntax or BaseObjectCreationExpressionSyntax
            || receiver is not null && model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol { IsSealed: true }
            || receiver is null && model.GetEnclosingSymbol(node.SpanStart, cancellationToken)?.ContainingType is { IsSealed: true };
        return new CallStep("call", symbols.Key(normalized), source ? SymbolNames.Label(normalized) : SymbolNames.SyntaxLabel(node), source, symbols.Location(node), children)
        { SuppressDispatch = exactReceiver };
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
