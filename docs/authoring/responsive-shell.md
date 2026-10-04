# Responsive authoring shell

The command bar wraps its existing actions into compact rows. Declared authoring actions wrap string labels inside the button content viewport while retaining their content, automation names and handlers. Programs remain in a scrolling rail with dirty markers. The Program route keeps sequence selection beside a flexible, vertically scrolling inspector; setting labels stack above their controls so the field retains its full width.

Hardware opens the existing program and instrument settings. Issues, Environment, and Build expose the existing finding, recovery/environment, and protected packaging views. Preview is a separate route. At client widths of at least 1180 logical pixels with normal text size (16 or below), the same preview instance also docks beside the editor when the Program route is selected. Larger text uses the separate preview route.

Preview tiles and recordings share a clipped, keyboard-focusable vertical viewport. When operation progress reduces the available height, existing chart, gauge and timing content keeps its natural size and recordings remain reachable by scrolling.

Focused views share the workspace view model and route actions through the existing window handlers. Operation progress/cancellation and guarded document transitions keep their existing services. Navigating back restores the focused editor control and brings it into view; the controls retain their scroll offsets and the session retains its sequence identity.

Protection dialogs measure the heading and wrapped decision rows around the scrolling body. Their safe default remains Cancel, including Escape and dismissal. They use the owner's text size instead of reserving a fixed allowance for heading and footer.

`ResponsiveShellTests` verifies actual client containment at 960×600 and 1280×800, including 150% render scaling, larger text and longer labels. It covers instrument/measurement/sampling/required-threshold controls, preview navigation, pointer-based focus and scroll restoration, many finding rows, and measured modal decision rows. `ResponsivePreviewTests` covers all three existing preview variants at both sizes with normal and larger text, including actual 150% rendering scale and real operations in progress. Tile and recordings bounds must fit the actual clipping viewport after scrolling them into view. Existing authoring UI tests retain many-program feedback and asynchronous operation/close-protection coverage.

`ResponsiveActionLabelTests` measures rendered text layout against actual button content bounds, including longer removal labels and active operations at larger fonts and rendering scale. It also requires the selected sequence row to remain fully visible with a usable sequence viewport.
