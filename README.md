# AnimeStudio - Hash Resolver Edition

This repository is a customized fork of AnimeStudio, originally derived from AssetStudio.

## Core Features Added

This specific fork implements a critical technical fix for resolving numeric CRC32 hashes into valid string paths for **Generic Animation Bindings** during YAML serialization. 

When exporting heavily compressed animations (e.g. ACL clips), certain animation engines or compression libraries discard the original bone string names and retain only their CRC32 numeric hashes. This results in .anim files containing unmapped path values (such as - path: 1552008890), which Unity's Generic Avatar system refuses to bind or evaluate properly.

### How it works:
1. **Embedded TOS Dictionary**: This project embeds a global dictionary (global_tos_dict.json) that maps pre-calculated numeric hashes back to their absolute Transform paths (e.g., Root/Bip001/Bip001_Pelvis/...).
2. **YAML Pipeline Interception**: During the final ConvertSerializedAnimationClip phase, the exporter scans for any numeric hashes in both standard Transform curves and Generic Muscle Bindings.
3. **Automated Restoration**: Unresolved hashes are dynamically replaced with valid string paths, ensuring that the exported .anim files are 100% plug-and-play within Unity's Generic Animation Rig.

## Credits & License

This project is built upon the incredible work of the open-source datamining and reverse-engineering community.
Special thanks to:
- **Escartem** & **yarik0chka** (AnimeStudio)
- **Razmoth**
- **Perfare** (Original AssetStudio)

This project is licensed under the **MIT License**. See the LICENSE file for full details.
