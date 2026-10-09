# Third-party anatomy assets

**BodyParts3D, © The Database Center for Life Science licensed under CC Attribution 4.0 International**

Source: [LSDB Archive license](https://dbarchive.biosciencedbc.jp/en/bodyparts3d/lic.html), updated 2025-02-27 and verified 2026-10-09; [CC BY 4.0 legal code](https://creativecommons.org/licenses/by/4.0/legalcode). Exact legal text and a dated license-page snapshot are in [docs/assets/bodyparts3d](assets/bodyparts3d/manifest.json). Attribution also accompanies the bundled application in `wwwroot/licenses/bodyparts3d.html` and the Profile credits link.

Geometry source: [Release 4.0 IS-A polygon archive](https://dbarchive.biosciencedbc.jp/en/bodyparts3d/data-7.html), DOI [10.18908/lsdba.nbdc00837-007](https://doi.org/10.18908/lsdba.nbdc00837-007), `isa_BP3D_4.0_obj_99.zip`, SHA-256 `40665852C49F218326590E204DB91064A1ECFC3C6F8CBD7BBBCAAC62C7CD409E`. Geometry dates to 2013. Its historical OBJ comments still say CC BY-SA 2.1 Japan; this project records that discrepancy and relies on the licensor's current explicit CC BY 4.0 grant for the database.

The distributed `makehuman-anatomical-muscle-fields-v1.bin` is treated as **adapted material** under CC BY 4.0. Changes: selected muscle surfaces registered to MakeHuman, projected to bounded skin displacement fields, symmetric reference handling and micrometre quantization. [Manifest](assets/bodyparts3d/manifest.json) records all selected object IDs, hashes and transformation versions; [mapping](BODYPARTS3D_MAPPING.md) records names and sides. No endorsement by the original creator is implied. No complete source archive or unused organ geometry is shipped.

MakeHuman's existing CC0 asset notice remains applicable to the target topology/skeleton. No Z-Anatomy data or code is used in this sidecar. Dataset anatomy is not a personalized scan or a medical measurement.
