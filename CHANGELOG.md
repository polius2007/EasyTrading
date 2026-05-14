# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0-alpha.1] — Phase 1: scaffolding

### Added
- Solution scaffold: `EasyTrading.slnx` with 6 projects (`Abstractions`, `Core`, `HyperLiquid`, `Broker`, unit tests, console sample).
- `EasyTrading.Abstractions` — cross-DEX contracts (`IExchangeClient` + sub-clients `IMarkets`, `IOrders`, `IPositions`, `ITrades`, `IAccount`, `ITransfers`, `IStreams`) and shared models.
- `EasyTrading.HyperLiquid` — `IHyperLiquidExchange` (extends `IExchangeClient`) adding `IVaults`, `IStaking`, `IBuilder`; client skeleton (real exchange calls land in Phase 2+).
- `EasyTrading.Core` — shared infrastructure project (HTTP, WebSocket, signing helpers — implementations land in Phase 2+).
- `EasyTrading.Broker` — builder-fee / rebate decorator project (implementation lands in Phase 5).
- Central package management via `Directory.Packages.props`.
- Shared package metadata via `Directory.Build.props`.
- MIT license.
- GitHub Actions workflows for CI and release.
- DocFX scaffold for the documentation site.

### Known limitations
- All `IExchangeClient` methods currently throw `NotImplementedException`. Real implementations land in Phase 2 onward.
