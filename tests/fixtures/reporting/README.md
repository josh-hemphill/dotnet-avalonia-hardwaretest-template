# Previously issued certification reports

These synthetic PDFs were created with the production `ITextPadesSignature.TrySign` from HardwareTest commit `7384f1323cea81329f3e9342857cce33c45111c9` using iText 9.7.0. The input was `PdfTestFixture.CreateMinimalPdf()`, and the signer used a fresh self-signed certificate from the corresponding `SoftwareSigningCertificate.CreateRsa`, `CreateP256` or `CreateP384` fixture. They contain public certificates only. The gzip layer preserves the original signed bytes.

| File | Uncompressed SHA-256 |
| --- | --- |
| itext-RSA.pdf.gz | `04d90c771c39d45f90f5a10a1c29ac2c8355f50a0e81f8fecfdab722fa9ce1a4` |
| itext-P256.pdf.gz | `9a42d1b9562665c9bd76c4f66b208d64217ee5990b71891018218cfad5575297` |
| itext-P384.pdf.gz | `224d0bae3e125b92264e811fd77be94b76e1e01cd6ec16f7eb6aff9ebec115aa` |
