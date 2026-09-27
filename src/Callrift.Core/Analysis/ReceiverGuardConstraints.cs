using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Callrift.Core;

internal sealed class ReceiverGuardConstraints(SemanticModel model, CancellationToken cancellationToken)
{
    private readonly Dictionary<ISymbol, Usage> usages = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<StatementSyntax, bool> terminatingStatements = [];

    public ITypeSymbol? Narrow(ExpressionSyntax receiver, ITypeSymbol? declared)
    {
        if (receiver is not IdentifierNameSyntax || model.GetSymbolInfo(receiver, cancellationToken).Symbol is not { } symbol
            || symbol is not (ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None })) return declared;
        var boundary = receiver.Ancestors().FirstOrDefault(IsFunction);
        if (boundary is null) return declared;
        if (!usages.TryGetValue(symbol, out var usage))
        {
            usage = InspectUsage(symbol, boundary);
            usages.Add(symbol, usage);
        }
        if (usage.Escapes || usage.Writes.Any(write => write.Ancestors().FirstOrDefault(IsFunction) != boundary)) return declared;
        var constraint = declared;
        foreach (var (condition, truth) in Conditions(receiver, boundary))
        {
            foreach (var (type, position) in Types(condition, truth, symbol))
            {
                var end = receiver.SpanStart;
                foreach (var loop in receiver.Ancestors().TakeWhile(node => node != boundary).Where(IsLoop))
                    if (!loop.Span.Contains(position)) end = Math.Max(end, loop.Span.End);
                if (usage.Writes.Any(write => write.SpanStart >= position && write.SpanStart < end)) continue;
                if (constraint is null || model.Compilation.ClassifyCommonConversion(type, constraint) is { IsImplicit: true, IsUserDefined: false })
                    constraint = type;
            }
        }
        return constraint;
    }

    private Usage InspectUsage(ISymbol symbol, SyntaxNode boundary)
    {
        var scope = boundary.AncestorsAndSelf().LastOrDefault(IsFunction) ?? boundary;
        var writes = new List<SyntaxNode>();
        var escapes = scope.DescendantNodes().Any(node => node is GotoStatementSyntax);
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier.Identifier.ValueText != symbol.Name
                || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, symbol)) continue;
            SyntaxNode target = identifier;
            while (target.Parent is ParenthesizedExpressionSyntax) target = target.Parent;
            if (target.Parent is ArgumentSyntax argument && argument.RefKindKeyword.RawKind != 0
                || target.Parent is RefExpressionSyntax) escapes = true;
            if (target.Parent is AssignmentExpressionSyntax assignment && assignment.Left == target
                || target.Parent is PrefixUnaryExpressionSyntax prefix && prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression
                || target.Parent is PostfixUnaryExpressionSyntax postfix && postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression)
                writes.Add(target);
            if (target.Ancestors().OfType<AssignmentExpressionSyntax>().Any(assignment => assignment.Left is TupleExpressionSyntax && assignment.Left.Span.Contains(target.Span)))
                writes.Add(target);
        }
        return new Usage(writes, escapes);
    }

    private IEnumerable<(ExpressionSyntax Condition, bool Truth)> Conditions(SyntaxNode receiver, SyntaxNode boundary)
    {
        for (var node = receiver; node.Parent is { } parent && node != boundary; node = parent)
        {
            switch (parent)
            {
                case IfStatementSyntax conditional when node == conditional.Statement:
                    yield return (conditional.Condition, true);
                    break;
                case ElseClauseSyntax { Parent: IfStatementSyntax conditional }:
                    yield return (conditional.Condition, false);
                    break;
                case ConditionalExpressionSyntax conditional when node == conditional.WhenTrue || node == conditional.WhenFalse:
                    yield return (conditional.Condition, node == conditional.WhenTrue);
                    break;
                case BinaryExpressionSyntax binary when node == binary.Right && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression
                    && model.GetOperation(binary, cancellationToken) is IBinaryOperation { OperatorMethod: null, Type.SpecialType: SpecialType.System_Boolean }:
                    yield return (binary.Left, binary.IsKind(SyntaxKind.LogicalAndExpression));
                    break;
                case BlockSyntax block when node is StatementSyntax statement:
                    foreach (var preceding in block.Statements.TakeWhile(candidate => candidate != statement).OfType<IfStatementSyntax>())
                    {
                        if (Terminates(preceding.Statement)) yield return (preceding.Condition, false);
                        if (preceding.Else is { } alternative && Terminates(alternative.Statement)) yield return (preceding.Condition, true);
                    }
                    break;
            }
        }
    }

    private bool Terminates(StatementSyntax statement)
    {
        if (!terminatingStatements.TryGetValue(statement, out var terminates))
        {
            terminates = model.AnalyzeControlFlow(statement) is { Succeeded: true, EndPointIsReachable: false };
            terminatingStatements.Add(statement, terminates);
        }
        return terminates;
    }

    private IEnumerable<(ITypeSymbol Type, int Position)> Types(ExpressionSyntax condition, bool truth, ISymbol receiver)
    {
        if (condition is ParenthesizedExpressionSyntax parenthesized)
            return Types(parenthesized.Expression, truth, receiver);
        if (condition is PrefixUnaryExpressionSyntax negation && negation.IsKind(SyntaxKind.LogicalNotExpression)
            && model.GetOperation(negation, cancellationToken) is IUnaryOperation { OperatorMethod: null })
            return Types(negation.Operand, !truth, receiver);
        if (condition is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression
            && model.GetOperation(binary, cancellationToken) is IBinaryOperation { OperatorMethod: null, Type.SpecialType: SpecialType.System_Boolean })
        {
            var left = Types(binary.Left, truth, receiver).ToArray();
            var right = Types(binary.Right, truth, receiver).ToArray();
            return binary.IsKind(SyntaxKind.LogicalAndExpression) == truth ? left.Concat(right)
                : left.Where(first => right.Any(second => SymbolEqualityComparer.Default.Equals(first.Type, second.Type)));
        }
        if (condition is IsPatternExpressionSyntax pattern && Matches(pattern.Expression, receiver))
            return PatternTypes(pattern.Pattern, truth).Select(type => (type, condition.Span.End));
        if (truth && condition is BinaryExpressionSyntax typeTest && typeTest.IsKind(SyntaxKind.IsExpression)
            && Matches(typeTest.Left, receiver) && model.GetTypeInfo(typeTest.Right, cancellationToken).Type is { TypeKind: not TypeKind.Error } type)
            return [(type, condition.Span.End)];
        return [];
    }

    private IEnumerable<ITypeSymbol> PatternTypes(PatternSyntax pattern, bool truth)
    {
        if (pattern is ParenthesizedPatternSyntax parenthesized) return PatternTypes(parenthesized.Pattern, truth);
        if (pattern is UnaryPatternSyntax negated) return PatternTypes(negated.Pattern, !truth);
        if (pattern is BinaryPatternSyntax binary)
        {
            var left = PatternTypes(binary.Left, truth).ToArray();
            var right = PatternTypes(binary.Right, truth).ToArray();
            return binary.IsKind(SyntaxKind.AndPattern) == truth ? left.Concat(right)
                : left.Where(first => right.Any(second => SymbolEqualityComparer.Default.Equals(first, second)));
        }
        var matchedType = model.GetOperation(pattern, cancellationToken) switch
        {
            ITypePatternOperation typed => typed.MatchedType,
            IDeclarationPatternOperation declared => declared.MatchedType,
            IRecursivePatternOperation recursive => recursive.MatchedType,
            _ => null
        };
        return truth && matchedType is { TypeKind: not TypeKind.Error } ? [matchedType] : [];
    }

    private bool Matches(ExpressionSyntax expression, ISymbol symbol)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
        return expression is IdentifierNameSyntax && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(expression, cancellationToken).Symbol, symbol);
    }

    private static bool IsFunction(SyntaxNode node) => node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;

    private static bool IsLoop(SyntaxNode node) => node is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax;

    private sealed record Usage(IReadOnlyList<SyntaxNode> Writes, bool Escapes);
}
