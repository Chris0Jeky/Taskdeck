# Smart CI continuation kit

Provider-neutral verification primitives for the existing Taskdeck Smart CI Fabric. This module does not post checks, execute task commands, change selection, or enable result reuse in Taskdeck.

From the repository root:

```sh
node --test scripts/ci/smart-ci/continuation.test.mjs
node scripts/ci/smart-ci/continuation/examples/demo.mjs
```

No installation or external dependencies are required. Node 22+ and Git are needed for the immutable-object test. The existing Smart CI Self-Test discovers the root bridge, which explicitly imports the nested suites.

Start with the [review and operator guide](../../../../docs/ci/continuation/OPERATIONS.md) and the [portable export guide](../../../../docs/ci/continuation/PORTABILITY.md).

Read the [engineering and integration contract](../../../../docs/ci/continuation/README.md) before using an enforce-mode result. The fictional example's reviewed contracts and ephemeral signing keys are test fixtures, not Taskdeck authorizations.

The kit inherits the repository's licensing. It is private/non-published; this change grants no separate package license and changes no existing license.
