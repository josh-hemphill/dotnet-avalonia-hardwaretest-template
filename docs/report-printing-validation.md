# Windows report printing acceptance

Native desktop acceptance has **not been run**. Automated tests use injected printer/viewer backends; real PDF raster tests render locally without opening a dialog or submitting a print job.

On a Windows desktop with a test printer, verify:

- Print opens an owned native dialog, including when no PDF viewer association exists. Cancel and Apply submit no document.
- A report longer than ten pages prints every selected page. Unordered/overlapping page ranges print each selected page once.
- Driver copies and collation work without duplicate application copies. Landscape and differing horizontal/vertical resolutions preserve physical page aspect and printable margins.
- A virtual PDF printer creates a new document outside the managed run folder. Save a copy preserves the exact issued bytes and original signature.
- Missing PDF associations open the owned application chooser without changing defaults. Cancelling the chooser does not report a successful open.
- Printer failures show failure and permit retry, Save a copy, and Open in PDF viewer; printing never falls back to a PDF viewer.
- Cancel during rendering or spooling aborts the document. During a blocking driver call, cancellation remains advisory: the job stays active and competing jobs remain blocked until the call returns; all native handles and page buffers then release.
- Navigating or selecting another revision while printing cancels the captured job without printing the newly selected report. A successful EndDoc reports only “Submitted to printer.”

Linux/macOS acceptance also remains unrun: verify `lp` receives the exact file path and a nonzero exit code reports failure rather than submission.
