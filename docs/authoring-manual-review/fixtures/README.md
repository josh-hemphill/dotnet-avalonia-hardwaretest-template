# Manual review assets

These files are preparation for the complete stack, not evidence of a completed manual review.

`legacy-recordings/mean-vdc/run.json` and `legacy-recordings/vdc-elapsed/run.json` are unchanged copies of existing authoring fixtures. They exercise legacy recording import, missing provenance and elapsed-time handling. They do not provide producer GUID/step-run evidence.

The compiled fixtures below provide matching source and recorded execution for scoped board review. Final handoff must identify the exact top-stack branch/commit, launch/build commands and which assets were exercised.

`package17-compiled/pass` and `package17-compiled/fail` contain actual compiled-plan recordings from the scoped parity regression, plus matching `board.TapPlan`, sidecar, durable authoring source and workspace manifest. Each recording contains 20 samples, four producer IDs, eight distinct step-run IDs and iterations 1 and 2; recorded outcomes are Passed and Failed respectively. DUT identity is absent in these recordings and should be shown as unavailable.

Open either folder as a workspace, then import its `run.json` through the recording import action. This checks the real import path rather than silently pre-populating the recordings catalog. The files alone are evidence of the compiled regression, not completed desktop interaction.

`invalid-import/run.json` deliberately contains a nonnumeric measurement value. Import it after selecting a valid recording and verify the error preserves the prior recording and destination bytes. Re-import the same valid source to exercise an existing-destination conflict.

Package17 compiled nested-repeat workspace: `package17-compiled/nested-repeat`, with real source/plan, recording and cassette evidence. Its four separate derived means, actual loop invocation GUIDs and scoped event marks provide a manual check for nested execution separation. See that folder README for observed counts and honest runtime/desktop evidence boundaries.
