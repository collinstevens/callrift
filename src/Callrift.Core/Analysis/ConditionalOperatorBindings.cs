using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Callrift.Core;

internal sealed class ConditionalOperatorBindings(CancellationToken cancellationToken)
{
    private readonly Dictionary<SyntaxNode, IReadOnlyDictionary<(TextSpan Span, UnaryOperatorKind Kind), IUnaryOperation>> bindings = [];

    public IUnaryOperation? Find(IBinaryOperation binary)
    {
        var root = (IOperation)binary;
        while (root.Parent is { } parent) root = parent;
        if (!bindings.TryGetValue(root.Syntax, out var operators))
        {
            var collected = new Dictionary<(TextSpan, UnaryOperatorKind), IUnaryOperation>();
            var graph = root switch
            {
                IMethodBodyOperation body => ControlFlowGraph.Create(body, cancellationToken),
                IConstructorBodyOperation body => ControlFlowGraph.Create(body, cancellationToken),
                IBlockOperation block => ControlFlowGraph.Create(block, cancellationToken),
                IFieldInitializerOperation initializer => ControlFlowGraph.Create(initializer, cancellationToken),
                IPropertyInitializerOperation initializer => ControlFlowGraph.Create(initializer, cancellationToken),
                IParameterInitializerOperation initializer => ControlFlowGraph.Create(initializer, cancellationToken),
                _ => null
            };
            if (graph is not null) Collect(graph, collected);
            bindings[root.Syntax] = operators = collected;
        }
        return operators.GetValueOrDefault((binary.LeftOperand.Syntax.Span,
            binary.OperatorKind == BinaryOperatorKind.ConditionalAnd ? UnaryOperatorKind.False : UnaryOperatorKind.True));
    }

    private void Collect(ControlFlowGraph graph, Dictionary<(TextSpan, UnaryOperatorKind), IUnaryOperation> collected)
    {
        foreach (var block in graph.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var operation in block.Operations) CollectOperation(operation, graph, collected);
            if (block.BranchValue is { } branch) CollectOperation(branch, graph, collected);
        }
        foreach (var local in graph.LocalFunctions) Collect(graph.GetLocalFunctionControlFlowGraph(local, cancellationToken), collected);
    }

    private void CollectOperation(IOperation operation, ControlFlowGraph graph, Dictionary<(TextSpan, UnaryOperatorKind), IUnaryOperation> collected)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation is IUnaryOperation { IsImplicit: true, OperatorMethod: not null } unary
            && unary.OperatorKind is UnaryOperatorKind.True or UnaryOperatorKind.False)
            collected[(unary.Syntax.Span, unary.OperatorKind)] = unary;
        if (operation is IFlowAnonymousFunctionOperation lambda)
            Collect(graph.GetAnonymousFunctionControlFlowGraph(lambda, cancellationToken), collected);
        foreach (var child in operation.ChildOperations) CollectOperation(child, graph, collected);
    }
}
