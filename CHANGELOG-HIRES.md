# slskd high-res streaming endpoints (HIRES changelog)

Adds phone-streamable byte serving for completed downloads plus progressive streaming of
in-progress downloads. All routes use `[Authorize(Policy = AuthPolicy.Any)]`; relay-agent
mode returns 403.

## New endpoints

- `GET api/v0/files/downloads/files/{base64FilePath}` (`Files/API/FilesController.cs`)
  Standard or URL-safe base64 of the downloads-relative path. `FileSafety.CombineSafely` +
  containment backstop (400/403), 404 when missing, `PhysicalFile(..., enableRangeProcessing:
  true)` for 200/206/416.
- `GET api/v0/transfers/downloads/stream/{username}/{id}`
  (`Transfers/API/Controllers/TransfersController.cs`)
  One URL for the whole transfer lifecycle: serves the growing incomplete file while
  downloading (blocks for not-yet-written bytes, refreshes transfer state, gives up cleanly
  with 404 if nothing was served instead of a short 200), then the finished file under the
  batch destination after the move. Full single-range support via `StreamRange`
  (`Transfers/API/TransferStreamRange.cs`); suffix ranges supported; multi-range rejected
  with 416 + `Content-Range: bytes */total`.

## Behavior change

- `Transfers/Downloads/DownloadService.cs`: incomplete files are created with
  `FileShare.Read` (was `None`) so readers can stream concurrently; the writer still holds
  exclusive write access. Path resolution factored into `GetIncompleteFilename()` shared by
  the download flow and the stream endpoint.

## Tests

- `FilesControllerTests` (9): traversal/invalid-base64 → 400, missing/escaped → 404, full
  200 byte-exact, `bytes=100-199` → 206 with exact `Content-Range`, over-range → 416,
  URL-safe base64 accepted.
- `TransferStreamRangeTests` (12): full/suffix/clamped ranges, all 416 shapes, non-positive total.
- `TransfersControllerStreamTests` (7): 400/404/416 shapes, full 200, partial 206,
  post-move final-file serving, already-failed transfer → clean 404.
- Full suite: 1317/1317 pass.

## App contract notes (for the Metrolist client)

- `searchTimeout` is passed straight into Soulseek.NET's **millisecond** field despite being
  documented as seconds — clients should send milliseconds (e.g. 12000–15000).
- Enqueue into an explicit `Destination` per batch so the finished file is locatable.
- A transfer that ends `Completed, Rejected` (e.g. "File not shared") has no bytes; pick the
  next ranked candidate.
