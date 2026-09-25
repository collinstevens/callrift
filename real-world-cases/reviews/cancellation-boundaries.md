# Cancellation after analysis

The tree query returned a result for a valid request with a pre-cancelled token. Diff also returned a result when an analysis provider cancelled the request while completing its second graph. Call enumeration could continue after cancellation in both tree and reach. These reproductions use deterministic cancellation boundaries rather than timing or sleeps.

Tokens now reach change detection, root selection, dispatch filtering, expansion, depth-limit reachability, alignment, presentation, truncation, and query traversal. Comparison checks cancellation after analysis and before returning its result. The reachability cache checks the token again after acquiring its lock. Existing public overloads and the three-argument `TreeExpander` constructor remain available; new overloads accept a token without replacing their signatures.

Seven boundary regressions exercise pre-cancelled tree/reach requests, analysis completion, tree/reach call enumeration, and both fingerprint and root-discovery traversal in diff. Six fail against the previous implementation. The pre-cancelled reach control already passes. All seven pass with token propagation.

The integrated implementation passes 65 targeted scenario/query/depth/cancellation tests, all 22 workspace checks, and all 51 previously accepted real-world checks without snapshot changes. Package smoke and formatting also pass. These checks cover the graph-processing cancellation gap. They do not establish cancellation coverage for every renderer, restore, or worker-process boundary.
