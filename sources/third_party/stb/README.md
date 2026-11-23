# stb_image

Unmodified upstream `stb_image.h` v2.30, fetched 2026-10-07 from
https://raw.githubusercontent.com/nothings/stb/master/stb_image.h

SHA-256: `594c2fe35d49488b4382dbfaec8f98366defca819d916ac95becf3e75f4200b3`.
The upstream file includes its dual public-domain/MIT license.
Only the TGA memory decoder is compiled, in the isolated Vulkan source adapter.

`stb_dxt.h` is the current upstream v1.12 file at commit
`48fb1c9709f973041a7e6992d720ac625266cdd3`, fetched 2026-11-13. Its SHA-256 is
`8f1a6cc3af7057679a36d32915aff15894521170bcdf95f47c101fdbb1f90500`.
The source includes its dual public-domain/MIT license. Vulkan source texture
conversion uses its BC1/BC3 compressor; the adapter supplies BC2 explicit alpha,
BC1a transparency and layer-major cube/mip handling. Provenance is recorded in
`dxt-provenance.json`.
