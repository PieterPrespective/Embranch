# Changelog

All notable changes to Embranch will be documented in this file.

<a name="1.0.5"></a>
## [1.0.5](https://www.github.com/PieterPrespective/Embranch/releases/tag/v1.0.5) (2026-02-02)

### Bug Fixes

* **dolt:** resolve CLI parsing and merge reconciliation issues (PP13-95, PP13-96) ([0d40432](https://www.github.com/PieterPrespective/Embranch/commit/0d4043222e16959a5b01b7e02c495d9f6b5fb1b8))

<a name="1.0.4"></a>
## [1.0.4](https://www.github.com/PieterPrespective/Embranch/releases/tag/v1.0.4) (2026-02-02)

### Features

* basic MCP Server Setup ([ef783d3](https://www.github.com/PieterPrespective/Embranch/commit/ef783d353aa7a0effc04441d6578068db42b7ee5))
* implemented chroma and dolt delta detector ([73cd5b8](https://www.github.com/PieterPrespective/Embranch/commit/73cd5b862fad6111c63cf6b2ec47a39eaef3150e))
* implemented Dolt CLI interfacing ([2fb5f6d](https://www.github.com/PieterPrespective/Embranch/commit/2fb5f6d6f71ce531e766b626b10c831dd971ea73))
* implemented primary chroma database tools ([5e77672](https://www.github.com/PieterPrespective/Embranch/commit/5e77672ef55f7c35e02063ca077ff0d5e97995b2))
* Rename DMMS namespace to Embranch ([887fc22](https://www.github.com/PieterPrespective/Embranch/commit/887fc22f8e93375b5e75ed4fce68aa58effe8950))

### Bug Fixes

* **bootstrap:** resolve fresh clone and sync issues (PP13-87-C2) ([7a152d1](https://www.github.com/PieterPrespective/Embranch/commit/7a152d1e318b7662e617b7c43ff49d6863ff8a7b))
* **chroma:** resolve ListCollections collection name parsing bug (PP13-89) ([995e9b6](https://www.github.com/PieterPrespective/Embranch/commit/995e9b6e3455c8cd3c89588e5d88da582e8548c8))
* **docs:** correct environment variable names in README ([782eed0](https://www.github.com/PieterPrespective/Embranch/commit/782eed0b91afb58db6f503cc1faa0bd309ff8ea9))
* **merge:** handle LocalChangesExist status and update sync state (PP13-90) ([fb48541](https://www.github.com/PieterPrespective/Embranch/commit/fb485411f570ef290af283d303be7c9ee12135e8))

### Maintenance

* add GitHub Actions workflow for build and release ([fbb3fa4](https://www.github.com/PieterPrespective/Embranch/commit/fbb3fa4ffc7791d8e41a870540fc95f08500ff24))
* configure versionize for automated versioning ([8507462](https://www.github.com/PieterPrespective/Embranch/commit/850746275a3aa48da4e21b69f33ed42f9875e356))
* **release:** 1.0.0 ([3b38d88](https://www.github.com/PieterPrespective/Embranch/commit/3b38d8806cee449de4a71997aac2da7bb1c4ce0d))
* **release:** 1.0.1 ([9eefc1d](https://www.github.com/PieterPrespective/Embranch/commit/9eefc1d1880b5163cbbe676a97aeacfec8d98b3c))
* **release:** 1.0.2 ([8748a4c](https://www.github.com/PieterPrespective/Embranch/commit/8748a4c9ca6a07263beb6466ba2a25f407e1a868))
* **release:** 1.0.3 ([f988f18](https://www.github.com/PieterPrespective/Embranch/commit/f988f185734c7d680f332f75c40c57b101af3fdf))

### Documentation

* rebrand README from VMRAG to Embranch ([6416f17](https://www.github.com/PieterPrespective/Embranch/commit/6416f171975f97b13a1f6cdc1ecf4c05195ec4e0))
* setup user docs & tdd ([94ea299](https://www.github.com/PieterPrespective/Embranch/commit/94ea2994924f8115675e839ef16537b8a8d4b7e2))
* **changelog:** update historical entries and fix URLs ([fee49e5](https://www.github.com/PieterPrespective/Embranch/commit/fee49e56f612a43affc3591f3e93f3ee2e6969b3))

<a name="1.0.3"></a>
## [1.0.3](https://www.github.com/PieterPrespective/Embranch/releases/tag/v1.0.3) (2026-01-31)

### Bug Fixes

* **chroma:** fix ListCollectionsAsync returning Python __repr__ format (PP13-89)
  - Fixed collection name parsing to handle ChromaDB v0.6.0+ API changes
  - Resolves Linux platform change detection and commit failures
  - Added backward compatibility for older ChromaDB versions

### Tests

* add PP13_89_ListCollectionsNameFormatTests integration tests

<a name="1.0.2"></a>
## [1.0.2](https://www.github.com/PieterPrespective/Embranch/releases/tag/v1.0.2) (2026-01-29)

### Bug Fixes

* **bootstrap:** resolve fresh clone and sync issues (PP13-87-C2) ([7a152d1](https://www.github.com/PieterPrespective/Embranch/commit/7a152d1e318b7662e617b7c43ff49d6863ff8a7b))

### Documentation

* **changelog:** update historical entries and fix URLs ([fee49e5](https://www.github.com/PieterPrespective/Embranch/commit/fee49e56f612a43affc3591f3e93f3ee2e6969b3))

<a name="1.0.1"></a>
## [1.0.1](https://www.github.com/PieterPrespective/Embranch/releases/tag/v1.0.1) (2026-01-26)

### Bug Fixes

* **docs:** correct environment variable names in README ([782eed0](https://www.github.com/PieterPrespective/Embranch/commit/782eed0b91afb58db6f503cc1faa0bd309ff8ea9))

### Maintenance

* add GitHub Actions workflow for build and release ([fbb3fa4](https://www.github.com/PieterPrespective/Embranch/commit/fbb3fa4ffc7791d8e41a870540fc95f08500ff24))

### Documentation

* rebrand README from VMRAG to Embranch ([6416f17](https://www.github.com/PieterPrespective/Embranch/commit/6416f171975f97b13a1f6cdc1ecf4c05195ec4e0))

<a name="1.0.0"></a>
## [1.0.0](https://www.github.com/PieterPrespective/Embranch/releases/tag/v1.0.0) (2026-01-25)

### Features

* basic MCP Server Setup ([ef783d3](https://www.github.com/PieterPrespective/Embranch/commit/ef783d353aa7a0effc04441d6578068db42b7ee5))
* implemented primary chroma database tools ([5e77672](https://www.github.com/PieterPrespective/Embranch/commit/5e77672ef55f7c35e02063ca077ff0d5e97995b2))
* refactored chroma to marshall via python.net ([5fa5eba](https://www.github.com/PieterPrespective/Embranch/commit/5fa5eba))
* auto-chroma db migration, moved python to own thread ([ac25b6a](https://www.github.com/PieterPrespective/Embranch/commit/ac25b6a))
* cleaned mcp tool JSON responses ([aeeb5a5](https://www.github.com/PieterPrespective/Embranch/commit/aeeb5a5))
* implemented Dolt CLI interfacing ([2fb5f6d](https://www.github.com/PieterPrespective/Embranch/commit/2fb5f6d6f71ce531e766b626b10c831dd971ea73))
* properly implemented Dolthub interface + manual test ([7bac89d](https://www.github.com/PieterPrespective/Embranch/commit/7bac89d))
* implemented chroma and dolt delta detector ([73cd5b8](https://www.github.com/PieterPrespective/Embranch/commit/73cd5b862fad6111c63cf6b2ec47a39eaef3150e))
* Implemented bi-directional support for chroma and dolt ([92008e3](https://www.github.com/PieterPrespective/Embranch/commit/92008e3))
* updated user-facing tools to support flow ([733c670](https://www.github.com/PieterPrespective/Embranch/commit/733c670))
* implemented extensive tool input output logging ([d07e817](https://www.github.com/PieterPrespective/Embranch/commit/d07e817))
* added Preview- and execute merge tools ([ecb75c6](https://www.github.com/PieterPrespective/Embranch/commit/ecb75c6))
* implemented Import tool for pre-existing chroma databases ([34adb6f](https://www.github.com/PieterPrespective/Embranch/commit/34adb6f))
* added manifest for auto-sync to repo, branch and commit ([6e98a16](https://www.github.com/PieterPrespective/Embranch/commit/6e98a16))
* Rename DMMS namespace to Embranch ([887fc22](https://www.github.com/PieterPrespective/Embranch/commit/887fc22f8e93375b5e75ed4fce68aa58effe8950))

### Bug Fixes

* resolved issues in E2E sync test, now works ([fcbd16d](https://www.github.com/PieterPrespective/Embranch/commit/fcbd16d))
* fixed locking on Syncmanager ([7812b1f](https://www.github.com/PieterPrespective/Embranch/commit/7812b1f))
* Failed Clone doesn't create 2nd dolt repo ([26510c1](https://www.github.com/PieterPrespective/Embranch/commit/26510c1))
* invalid push address parsing ([d934abf](https://www.github.com/PieterPrespective/Embranch/commit/d934abf))
* Faulty attempt to remove 'default' collection after filled remote ([cc508b1](https://www.github.com/PieterPrespective/Embranch/commit/cc508b1))
* Fixed issue with syncing filled database ([28cd656](https://www.github.com/PieterPrespective/Embranch/commit/28cd656))
* Fixed issue with flipped id and content fields chroma and dolt ([3741a95](https://www.github.com/PieterPrespective/Embranch/commit/3741a95))
* Fixed unwanted pythoncontext error throwing in tests ([d593182](https://www.github.com/PieterPrespective/Embranch/commit/d593182))
* All tests fully functional again ([c93ca00](https://www.github.com/PieterPrespective/Embranch/commit/c93ca00))
* Implemented fix to detect added documents as commitable ([2eca05a](https://www.github.com/PieterPrespective/Embranch/commit/2eca05a))
* Documents now sync properly chroma to dolt on commit ([6177d0b](https://www.github.com/PieterPrespective/Embranch/commit/6177d0b))
* implemented document change and removal tracking ([ba4a112](https://www.github.com/PieterPrespective/Embranch/commit/ba4a112))
* Implemented collection change and removal tracking ([af84b5a](https://www.github.com/PieterPrespective/Embranch/commit/af84b5a))
* Chroma not reset after dolt branch reset ([677ca66](https://www.github.com/PieterPrespective/Embranch/commit/677ca66))
* Fixed issues surrounding document Chroma CRUD ([f68fa29](https://www.github.com/PieterPrespective/Embranch/commit/f68fa29))
* Merge conflict resolution no longer loses changes ([14bb4bf](https://www.github.com/PieterPrespective/Embranch/commit/14bb4bf))
* fixed issue with multi-instance access to chroma dbs in tests ([930838b](https://www.github.com/PieterPrespective/Embranch/commit/930838b))
* fixed issue with illegal characters in imported documents on windows ([c92a797](https://www.github.com/PieterPrespective/Embranch/commit/c92a797))
* fixed issue with JSON object blocking MCP server restart ([0e7ba55](https://www.github.com/PieterPrespective/Embranch/commit/0e7ba55))
* Planned Embranch namechange ([005c57a](https://www.github.com/PieterPrespective/Embranch/commit/005c57a))
* Regression test fixes after Embranch rename ([d8e5832](https://www.github.com/PieterPrespective/Embranch/commit/d8e5832))
* fixed some additional renames, installed versionize ([a696a9c](https://www.github.com/PieterPrespective/Embranch/commit/a696a9c))
* no longer falsely auto-inits when no remote set in manifest ([c10497c](https://www.github.com/PieterPrespective/Embranch/commit/c10497c))

### Maintenance

* configure versionize for automated versioning ([8507462](https://www.github.com/PieterPrespective/Embranch/commit/850746275a3aa48da4e21b69f33ed42f9875e356))
* Installed Versionize ([07caf8a](https://www.github.com/PieterPrespective/Embranch/commit/07caf8a))

### Documentation

* setup user docs & tdd ([94ea299](https://www.github.com/PieterPrespective/Embranch/commit/94ea2994924f8115675e839ef16537b8a8d4b7e2))
* updated user docs with all mcp tools and init sequence ([35caebc](https://www.github.com/PieterPrespective/Embranch/commit/35caebc))

