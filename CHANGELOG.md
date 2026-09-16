# Changelog
All notable changes to this project will be documented in this file.

---
# Release - published

## [1.0.3] - 2026-09-17
### Modified
- Sample updated.

## [1.0.2] - 2026-09-16
### Fixed
- [Menu] The `Tools > AceLand > Lifecycle > License` item is now its own separate group, matching the grouping used by other AceLand tools. The Lifecycle submenu now shows three groups (License / graph & timeline tools / validation) instead of two.
### Notes
- Requires AceLand Licensing 1.0.1+, which fixes reliable per-product rebinding of the shared License window (opening a second product's License menu now correctly switches the window content and no longer blocks starting that product's trial).

## [1.0.1] - 2026-09-16
### Modified
- Licensing is now an OPTIONAL dependency. Installing AceLand Lifecycle no longer force-installs AceLand Licensing. The runtime and builds are unaffected as always; the paid Editor tools (Initialization Graph, Player Loop Graph, Initialization Timeline, Quit Pipeline Graph, dependency validation) simply stay disabled and show a one-click install prompt until AceLand Licensing is present.
- When AceLand Licensing is installed, the Editor tools automatically restore their full license flow — no manual setup required.

## [1.0.0] - 2026-09-12
### Added
- Open Core & Paid Editor Tools model: the runtime (Bootstrap, dependency-ordered initialization, quit pipeline, player-loop scheduling) stays free & open source forever; only the development-time Editor tools (Initialization Graph, Player Loop Graph, Initialization Timeline, Quit Pipeline Graph, dependency validation) require a paid AceLand Lifecycle license.
- Editor licensing flow (subscription and one-time perpetual purchase) with offline signed-token verification, machine-id seat binding, and a reusable license window, powered by the shared AceLand licensing Core.
- Friendly degradation: unlicensed editors keep the runtime fully functional; gated Editor windows show a purchase/activation page and auto-validate silently skips.
- [Player Loop Graph] Error nodes now use a push model: the runtime raises an internal event the moment a frame process errors and removes the handle immediately, keeping no error state of its own. The Player Loop Graph window owns all retention timing — the 5-second mode is timed editor-side, and Keep mode holds error nodes only while the player-loop driver is installed (cleared on self-heal / stop Play). If the window is not open the error is not retained (still logged).
### Notes
- First stable (non-experimental) release. Runtime and builds are never gated.

---
# Exp - published

## [0.3.2] - 2026-08-26
### Modified
- code optimize

## [0.3.1] - 2026-08-24
### Fixed
- Fixed sorter side effect on editor tester

## [0.3.0] - 2026-08-20
### Added
- [PlayerLoop] porting from Player Loop Hack package
- [Player Loop Graph] a graph for live monitor of player loop processes status
- [Initialization Timeline] a editor profiler for deep optimization of initialization modules
### Modified
- [Sample] updated with PlayerLoop
- [Graph] source tracing action and context menu on node, search filter
- [Quit Pipeline Graph] node-base graph now

--
# Beta - published

## [0.2.5] - 2026-08-14
- code organize
- [Sample] improve scene

## [0.2.4] - 2026-08-14
- [Module] fixed module running not affected Unity runtime phase
- [Module] add event to listen for all module phase completed
- [Sample] all module phase sample and summary

## [0.2.3] - 2026-08-13
- [Quit Pipeline] fixed build issue - hanged on safe quit process on empty queue

## [0.2.2] - 2026-08-13
- [Sample] add `AssemblyInfo.cs`

## [0.2.1] - 2026-08-12
- fix quit pipeline windows layout

## [0.2.0] - 2026-08-12
- beta published

---
# Dev - unpublished
## [0.1.1] - 2026-08-12
dev optimize and bug fix

## [0.1.0] - 2026-08-11
Repo created, project in dev level, not published.   
For detail please visit and bookmark our [GitBook](https://aceland-workshop.gitbook.io/aceland-unity-packages/)-