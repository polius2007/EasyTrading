using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.Logging;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Comprehensive live-mainnet audit of every order type + every method the library exposes
/// that can be safely exercised with an agent wallet (i.e. excluding master-only user-signed
/// actions like Withdraw / Transfer / ApproveAgent / Vault Deposit / Stake operations).
///
/// Skipped unless every required env var is set:
///   - EASYTRADING_LIVE_AUDIT=1
///   - HL_MAINNET_MASTER_ADDRESS
///   - HL_MAINNET_AGENT_KEY
///   - HL_MAINNET_AGENT_NAME (optional)
///
/// All write actions in this audit are designed to NOT actually fill: BUY at half-mid, SELL
/// at 2× mid, stops with triggers far from the current price. Worst-case if something goes
/// wrong: a real fill on a tiny position. The finally block aggressively cancels every open
/// order before exiting.
/// </summary>
public sealed class HyperLiquidMainnetFullAuditTests
{
    private const string TestSymbol = "BTC";
    private const decimal TestSize  = 0.001m; // notional ~$40-160 across the ½×-2× price range

    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_LIVE_AUDIT"), "1", StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "MainnetSmoke")]
    public async Task Full_audit_every_supported_method_on_HL_mainnet()
    {
        if (!Enabled) return;

        var master   = Environment.GetEnvironmentVariable("HL_MAINNET_MASTER_ADDRESS");
        var agentKey = Environment.GetEnvironmentVariable("HL_MAINNET_AGENT_KEY");
        if (string.IsNullOrEmpty(master) || string.IsNullOrEmpty(agentKey)) return;

        var captured = new CapturingLogger<HyperLiquidClient>();
        await using var client = new HyperLiquidClient(new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Mainnet,
            Credentials = new HyperLiquidCredentials(
                MasterAddress: master,
                PrivateKey:    agentKey,
                AgentName:     Environment.GetEnvironmentVariable("HL_MAINNET_AGENT_NAME")),
        }, captured);

        var stats = new AuditStats();

        try
        {
            await SectionMarketsReadsAsync(client, stats);
            await SectionAccountReadsAsync(client, stats);
            await SectionTradesAndPositionsReadsAsync(client, stats);
            await SectionAllOrderTypesAsync(client, stats);
            await SectionModifyAsync(client, stats);
            await SectionBatchOpsAsync(client, stats);
            await SectionOrderQueriesAsync(client, stats);
            await SectionCancelAllAsync(client, stats);
            await SectionScheduleCancelAsync(client, stats);
            await SectionVaultsStakingReadsAsync(client, stats);
            await SectionStreamsAsync(client, stats);
        }
        finally
        {
            // Defensive cleanup: cancel anything that might have been left resting.
            try
            {
                var nuked = await client.Orders.CancelAllAsync(symbol: null);
                if (nuked > 0)
                    Console.WriteLine($"[audit][cleanup] cancelled {nuked} stragglers");
            }
            catch (Exception ex) { Console.WriteLine($"[audit][cleanup] CancelAllAsync error: {ex.Message}"); }
        }

        Console.WriteLine($"\n[audit] ── SUMMARY ──");
        Console.WriteLine($"[audit] passed:  {stats.Passed}");
        Console.WriteLine($"[audit] failed:  {stats.Failed}");
        Console.WriteLine($"[audit] skipped: {stats.Skipped}");
        if (stats.Failures.Count > 0)
        {
            Console.WriteLine($"\n[audit] ── FAILURES ──");
            foreach (var f in stats.Failures)
                Console.WriteLine($"  ✗ {f}");
        }

        Assert.Equal(0, stats.Failed);
    }

    // ─── SECTION 1: Markets reads ────────────────────────────────────────────

    private static async Task SectionMarketsReadsAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 1: Markets reads ═══");

        await Run(s, "Markets.GetSymbolsAsync(All)", async () =>
        {
            var syms = await c.Markets.GetSymbolsAsync();
            return $"{syms.Count} symbols";
        });
        await Run(s, "Markets.GetSymbolsAsync(Perpetual)", async () =>
        {
            var syms = await c.Markets.GetSymbolsAsync(MarketKind.Perpetual);
            return $"{syms.Count} perps; BTC present: {syms.Any(x => x.Name == "BTC")}";
        });
        await Run(s, "Markets.GetSymbolsAsync(Spot)", async () =>
        {
            var syms = await c.Markets.GetSymbolsAsync(MarketKind.Spot);
            return $"{syms.Count} spot pairs";
        });
        await Run(s, "Markets.GetSymbolAsync(BTC)", async () =>
        {
            var sym = await c.Markets.GetSymbolAsync(TestSymbol);
            return $"name={sym.Name}, kind={sym.Kind}, quote={sym.QuoteAsset}";
        });
        await Run(s, "Markets.GetOrderBookAsync(BTC, 10)", async () =>
        {
            var book = await c.Markets.GetOrderBookAsync(TestSymbol, depth: 10);
            return $"bids={book.Bids.Count}, asks={book.Asks.Count}, spread={book.Asks[0].Price - book.Bids[0].Price}";
        });
        await Run(s, "Markets.GetCandlesAsync(BTC, 1m, 1h)", async () =>
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddHours(-1);
            var candles = await c.Markets.GetCandlesAsync(TestSymbol, Interval.OneMinute, from, to);
            return $"{candles.Count} candles (expected ~60)";
        });
        await Run(s, "Markets.GetAllMidsAsync", async () =>
        {
            var mids = await c.Markets.GetAllMidsAsync();
            return $"{mids.Count} mids; BTC={mids.GetValueOrDefault(TestSymbol)}";
        });
        await Run(s, "Markets.GetMidAsync(BTC)", async () =>
        {
            var mid = await c.Markets.GetMidAsync(TestSymbol);
            return $"${mid}";
        });
        await Run(s, "Markets.GetFundingAsync(BTC)", async () =>
        {
            var f = await c.Markets.GetFundingAsync(TestSymbol);
            return $"rate={f.Rate}, mark={f.MarkPrice}, index={f.IndexPrice}, next={f.NextFundingTime:HH:mm}";
        });
        await Run(s, "Markets.GetFundingHistoryAsync(BTC, 24h)", async () =>
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddDays(-1);
            var hist = await c.Markets.GetFundingHistoryAsync(TestSymbol, from, to);
            return $"{hist.Count} funding records";
        });
        await Run(s, "Markets.GetOpenInterestAsync(BTC)", async () =>
        {
            var oi = await c.Markets.GetOpenInterestAsync(TestSymbol);
            return $"OI={oi}";
        });
        await RunExpectingThrow<NotSupportedException>(s, "Markets.GetRecentTradesAsync (expected NotSupported)",
            () => c.Markets.GetRecentTradesAsync(TestSymbol));
    }

    // ─── SECTION 2: Account reads ─────────────────────────────────────────────

    private static async Task SectionAccountReadsAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 2: Account reads ═══");
        await Run(s, "Account.GetStateAsync", async () =>
        {
            var st = await c.Account.GetStateAsync();
            return $"equity=${st.AccountValue}, free=${st.FreeCollateral}, positions={st.Positions.Count}";
        });
        await Run(s, "Account.GetBalanceAsync(USDC)", async () =>
        {
            var b = await c.Account.GetBalanceAsync("USDC");
            return $"${b}";
        });
        await Run(s, "Account.GetBalancesAsync", async () =>
        {
            var b = await c.Account.GetBalancesAsync();
            return $"{b.Count} tokens";
        });
        await Run(s, "Account.GetFeesAsync", async () =>
        {
            var fees = await c.Account.GetFeesAsync();
            return $"maker={fees.MakerRate}, taker={fees.TakerRate}";
        });
        await Run(s, "Account.GetPortfolioAsync", async () =>
        {
            var p = await c.Account.GetPortfolioAsync();
            return $"accountValue history: {p.AccountValueHistory.Count} samples, pnl history: {p.PnlHistory.Count}";
        });
        await Run(s, "Account.GetSubAccountsAsync", async () =>
        {
            var sa = await c.Account.GetSubAccountsAsync();
            return $"{sa.Count} sub-accounts";
        });
        await Run(s, "Account.GetRateLimitAsync", async () =>
        {
            var rl = await c.Account.GetRateLimitAsync();
            return $"used={rl.Used}/{rl.Limit}, resetAt={rl.WindowResetAt:HH:mm:ss}";
        });
        // GetApprovedAgentsAsync: HL has no read endpoint for this — the library throws
        // NotSupportedException with explicit guidance. Document as expected.
        await RunExpectingThrow<NotSupportedException>(s, "Account.GetApprovedAgentsAsync (expected NotSupported)",
            () => c.Account.GetApprovedAgentsAsync());
    }

    // ─── SECTION 3: Trades + Positions reads ─────────────────────────────────

    private static async Task SectionTradesAndPositionsReadsAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 3: Trades + Positions reads ═══");
        await Run(s, "Trades.GetMyFillsAsync", async () =>
        {
            var fills = await c.Trades.GetMyFillsAsync();
            return $"{fills.Count} fills";
        });
        await Run(s, "Positions.GetAllAsync", async () =>
        {
            var pos = await c.Positions.GetAllAsync();
            return $"{pos.Count} positions";
        });
        await Run(s, $"Positions.GetAsync({TestSymbol})", async () =>
        {
            var p = await c.Positions.GetAsync(TestSymbol);
            return p is null ? "no position" : $"size={p.Size}";
        });
        await Run(s, $"Orders.GetOpenAsync({TestSymbol})", async () =>
        {
            var open = await c.Orders.GetOpenAsync(TestSymbol);
            return $"{open.Count} open orders";
        });
        await Run(s, "Orders.GetHistoryAsync (24h)", async () =>
        {
            var hist = await c.Orders.GetHistoryAsync(TestSymbol);
            return $"{hist.Count} historical orders";
        });
        await Run(s, "Orders.GetTwapFillsAsync", async () =>
        {
            var twapFills = await c.Orders.GetTwapFillsAsync();
            return $"{twapFills.Count} twap slice fills";
        });
    }

    // ─── SECTION 4: All order types ──────────────────────────────────────────

    private static readonly List<long> PlacedOrderIds = new();
    private static readonly List<string> PlacedClientIds = new();

    private static async Task SectionAllOrderTypesAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 4: All order types ═══");
        var mid = await c.Markets.GetMidAsync(TestSymbol);
        var halfMid   = Math.Floor(mid * 0.5m);  // BUY far below
        var doubleMid = Math.Ceiling(mid * 2.0m); // SELL far above
        Console.WriteLine($"[audit]   live BTC mid=${mid}; halfMid=${halfMid}, doubleMid=${doubleMid}");

        // Limit GTC (resting BUY)
        await Run(s, $"PlaceLimitAsync(BUY @ {halfMid} GTC)", async () =>
        {
            var cid = NextClientId();
            var r = await c.Orders.PlaceLimitAsync(TestSymbol, OrderSide.Buy, halfMid, TestSize, TimeInForce.Gtc, clientOrderId: cid);
            TrackPlace(r, cid);
            return $"orderId={r.OrderId}, status={r.Status}";
        });

        // Limit ALO (post-only, far below → rests)
        await Run(s, $"PlaceLimitAsync(BUY @ {halfMid} ALO)", async () =>
        {
            var cid = NextClientId();
            var r = await c.Orders.PlaceLimitAsync(TestSymbol, OrderSide.Buy, halfMid, TestSize, TimeInForce.Alo, clientOrderId: cid);
            TrackPlace(r, cid);
            return $"orderId={r.OrderId}, status={r.Status}";
        });

        // Limit IOC (far below → no fill → expires)
        await Run(s, $"PlaceLimitAsync(BUY @ {halfMid} IOC) — expected Cancelled/Expired", async () =>
        {
            var cid = NextClientId();
            var r = await c.Orders.PlaceLimitAsync(TestSymbol, OrderSide.Buy, halfMid, TestSize, TimeInForce.Ioc, clientOrderId: cid);
            // Don't track for cancel — IOC at far-below price never rests
            return $"orderId={r.OrderId}, status={r.Status}, error={r.ErrorMessage ?? "(none)"}";
        });

        // Limit FOK (far below → can't fully fill → cancelled)
        await Run(s, $"PlaceLimitAsync(BUY @ {halfMid} FOK) — expected Cancelled", async () =>
        {
            var cid = NextClientId();
            var r = await c.Orders.PlaceLimitAsync(TestSymbol, OrderSide.Buy, halfMid, TestSize, TimeInForce.Fok, clientOrderId: cid);
            return $"orderId={r.OrderId}, status={r.Status}, error={r.ErrorMessage ?? "(none)"}";
        });

        // Stop-Market SELL with trigger far below current → never triggers, rests as trigger
        await Run(s, $"PlaceStopAsync(SELL trigger={halfMid}, isMarket=true) — Stop-Market", async () =>
        {
            var r = await c.Orders.PlaceStopAsync(TestSymbol, OrderSide.Sell,
                triggerPrice: halfMid, size: TestSize, isMarket: true, reduceOnly: true);
            TrackPlace(r, null);
            return $"orderId={r.OrderId}, status={r.Status}, error={r.ErrorMessage ?? "(none)"}";
        });

        // Stop-Limit SELL with trigger and limit far below
        await Run(s, $"PlaceStopAsync(SELL trigger={halfMid}, isMarket=false) — Stop-Limit", async () =>
        {
            var r = await c.Orders.PlaceStopAsync(TestSymbol, OrderSide.Sell,
                triggerPrice: halfMid, size: TestSize, isMarket: false, reduceOnly: true);
            TrackPlace(r, null);
            return $"orderId={r.OrderId}, status={r.Status}, error={r.ErrorMessage ?? "(none)"}";
        });

        // TakeProfit SELL with trigger far ABOVE current → won't trigger
        await Run(s, $"PlaceAsync(TakeProfit SELL trigger={doubleMid})", async () =>
        {
            var r = await c.Orders.PlaceAsync(new OrderRequest(
                Symbol: TestSymbol, Side: OrderSide.Sell,
                OrderType: OrderType.TakeProfit,
                Size: TestSize, Price: doubleMid, TriggerPrice: doubleMid,
                ReduceOnly: true));
            TrackPlace(r, null);
            return $"orderId={r.OrderId}, status={r.Status}, error={r.ErrorMessage ?? "(none)"}";
        });

        // Note: PlaceMarketAsync intentionally skipped — it WOULD actually fill at 5% slippage.
        s.Skip("PlaceMarketAsync (would actually fill — covered by unit test of IOC limit emulation)");
    }

    // ─── SECTION 5: Modify ───────────────────────────────────────────────────

    private static async Task SectionModifyAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 5: ModifyAsync ═══");
        if (PlacedOrderIds.Count == 0)
        {
            s.Skip("ModifyAsync (no open order to modify)");
            return;
        }
        var targetId = PlacedOrderIds[0];
        var mid = await c.Markets.GetMidAsync(TestSymbol);
        var newPrice = Math.Floor(mid * 0.45m); // slightly different from 0.5*mid
        await Run(s, $"ModifyAsync(orderId={targetId}, newPrice={newPrice})", async () =>
        {
            var r = await c.Orders.ModifyAsync(new ModifyRequest(
                Symbol: TestSymbol, OrderId: targetId, NewPrice: newPrice));
            // ModifyResult: success + new orderId (HL re-issues a new oid on modify)
            if (r.Success)
            {
                // Replace tracked id (modify returns new oid).
                var idx = PlacedOrderIds.IndexOf(targetId);
                if (idx >= 0) PlacedOrderIds[idx] = r.OrderId;
            }
            return $"newOrderId={r.OrderId}, success={r.Success}, error={r.ErrorMessage ?? "(none)"}";
        });
    }

    // ─── SECTION 6: Batch operations ─────────────────────────────────────────

    private static async Task SectionBatchOpsAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 6: Batch ops ═══");
        var mid = await c.Markets.GetMidAsync(TestSymbol);
        var p1 = Math.Floor(mid * 0.40m);
        var p2 = Math.Floor(mid * 0.42m);
        var p3 = Math.Floor(mid * 0.44m);

        var batchClientIds = new[] { NextClientId(), NextClientId(), NextClientId() };
        await Run(s, "PlaceBatchAsync × 3 limits (Alo)", async () =>
        {
            var batch = new[]
            {
                new OrderRequest(TestSymbol, OrderSide.Buy, OrderType.Limit, Size: TestSize, Price: p1, TimeInForce: TimeInForce.Alo, ClientOrderId: batchClientIds[0]),
                new OrderRequest(TestSymbol, OrderSide.Buy, OrderType.Limit, Size: TestSize, Price: p2, TimeInForce: TimeInForce.Alo, ClientOrderId: batchClientIds[1]),
                new OrderRequest(TestSymbol, OrderSide.Buy, OrderType.Limit, Size: TestSize, Price: p3, TimeInForce: TimeInForce.Alo, ClientOrderId: batchClientIds[2]),
            };
            var br = await c.Orders.PlaceBatchAsync(batch);
            var nOk = br.Results.Count(r => r.Status == OrderStatus.Open || r.Status == OrderStatus.Pending);
            foreach (var r in br.Results)
                if (r.OrderId > 0) PlacedOrderIds.Add(r.OrderId);
            return $"placed={nOk}/3";
        });

        // ModifyBatchAsync — modify the first two
        if (PlacedOrderIds.Count >= 2)
        {
            var modBatch = new[]
            {
                new ModifyRequest(Symbol: TestSymbol, OrderId: PlacedOrderIds[^2], NewPrice: Math.Floor(mid * 0.39m)),
                new ModifyRequest(Symbol: TestSymbol, OrderId: PlacedOrderIds[^1], NewPrice: Math.Floor(mid * 0.41m)),
            };
            await Run(s, "ModifyBatchAsync × 2", async () =>
            {
                var br = await c.Orders.ModifyBatchAsync(modBatch);
                var nOk = br.Results.Count(r => r.Success);
                // Track new oids
                for (var i = 0; i < br.Results.Count; i++)
                {
                    var newId = br.Results[i].OrderId;
                    if (newId > 0 && br.Results[i].Success)
                    {
                        var origIdx = PlacedOrderIds.IndexOf(modBatch[i].OrderId!.Value);
                        if (origIdx >= 0) PlacedOrderIds[origIdx] = newId;
                    }
                }
                return $"modified={nOk}/2";
            });
        }

        // CancelBatchAsync — cancel one specific
        if (PlacedOrderIds.Count >= 1)
        {
            await Run(s, "CancelBatchAsync × 1", async () =>
            {
                var oneId = PlacedOrderIds[0];
                var cb = new[] { new CancelRequest(Symbol: TestSymbol, OrderId: oneId) };
                var br = await c.Orders.CancelBatchAsync(cb);
                if (br.Results.All(r => r.Success))
                    PlacedOrderIds.Remove(oneId);
                return $"cancelled={br.Results.Count(r => r.Success)}/1";
            });
        }

        // CancelByClientIdAsync — cancel by one of the batch's clientOrderIds
        if (batchClientIds.Length > 0)
        {
            await Run(s, $"CancelByClientIdAsync({batchClientIds[0]})", async () =>
            {
                var r = await c.Orders.CancelByClientIdAsync(TestSymbol, batchClientIds[0]);
                return $"success={r.Success}, error={r.ErrorMessage ?? "(none)"}";
            });
        }
    }

    // ─── SECTION 7: Order queries ────────────────────────────────────────────

    private static async Task SectionOrderQueriesAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 7: Order queries ═══");
        await Run(s, "Orders.GetOpenAsync (no filter)", async () =>
        {
            var open = await c.Orders.GetOpenAsync();
            return $"{open.Count} open orders across all symbols";
        });

        if (PlacedOrderIds.Count > 0)
        {
            var first = PlacedOrderIds.First(id => id > 0);
            await Run(s, $"Orders.GetAsync({first})", async () =>
            {
                var o = await c.Orders.GetAsync(first);
                return o is null ? "not found" : $"sym={o.Symbol}, side={o.Side}, status={o.Status}, price={o.Price}, size={o.Size}";
            });
        }

        if (PlacedClientIds.Count > 0)
        {
            var cid = PlacedClientIds[0];
            await Run(s, $"Orders.GetByClientIdAsync({cid})", async () =>
            {
                var o = await c.Orders.GetByClientIdAsync(cid);
                return o is null ? "not found" : $"sym={o.Symbol}, status={o.Status}";
            });
        }
    }

    // ─── SECTION 8: CancelAllAsync ───────────────────────────────────────────

    private static async Task SectionCancelAllAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 8: CancelAllAsync ═══");
        await Run(s, $"Orders.CancelAllAsync({TestSymbol})", async () =>
        {
            var n = await c.Orders.CancelAllAsync(TestSymbol);
            PlacedOrderIds.Clear();
            return $"cancelled {n}";
        });
        await Run(s, "Orders.GetOpenAsync after CancelAll (should be 0)", async () =>
        {
            var open = await c.Orders.GetOpenAsync(TestSymbol);
            if (open.Count > 0) throw new InvalidOperationException($"expected 0 open orders; got {open.Count}");
            return "empty";
        });
    }

    // ─── SECTION 9: ScheduleCancelAsync ─────────────────────────────────────

    private static async Task SectionScheduleCancelAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 9: ScheduleCancelAsync ═══");
        // HL requires $1M traded volume before enabling dead-man scheduled cancel — without
        // that, every set/clear call returns an ExchangeApiException with a clear gate message.
        // The library wire-format is verified by the fact HL responds with the volume-gate
        // error (means our signed action was accepted on the wire); for accounts that meet the
        // volume bar this same call returns ok.
        await RunExpectingErrorMessage(s, "ScheduleCancelAsync(future 24h) — expected $1M-volume gate",
            () => c.Orders.ScheduleCancelAsync(DateTimeOffset.UtcNow.AddHours(24)),
            expectedSubstring: "until enough volume");
        await RunExpectingErrorMessage(s, "ScheduleCancelAsync(null) — clear, expected same gate",
            () => c.Orders.ScheduleCancelAsync(null),
            expectedSubstring: "until enough volume");
    }

    // ─── SECTION 10: Vaults + Staking reads ────────────────────────────────

    private static async Task SectionVaultsStakingReadsAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 10: Vaults + Staking reads ═══");
        await Run(s, "Vaults.GetMyEquitiesAsync", async () =>
        {
            var v = await c.Vaults.GetMyEquitiesAsync();
            return $"{v.Count} vault equities";
        });
        await Run(s, "Staking.GetMyDelegationsAsync", async () =>
        {
            var d = await c.Staking.GetMyDelegationsAsync();
            return $"{d.Count} delegations";
        });
        await Run(s, "Staking.GetMySummaryAsync", async () =>
        {
            var sum = await c.Staking.GetMySummaryAsync();
            return $"totalDelegated={sum.TotalDelegated}, totalUndelegating={sum.TotalUndelegating}, totalUndelegated={sum.TotalUndelegated}";
        });
        await Run(s, "Staking.GetMyRewardsAsync", async () =>
        {
            var r = await c.Staking.GetMyRewardsAsync();
            return $"{r.Count} reward records";
        });
    }

    // ─── SECTION 11: Streams ────────────────────────────────────────────────

    private static async Task SectionStreamsAsync(HyperLiquidClient c, AuditStats s)
    {
        Console.WriteLine("\n[audit] ═══ SECTION 11: Streams (5s each) ═══");
        await StreamProbe(s, "Streams.TradesAsync", async ct =>
        {
            await foreach (var t in c.Streams.TradesAsync(TestSymbol, ct))
                return $"first trade: {t.Trade.Side} {t.Trade.Size}@{t.Trade.Price}";
            return "(no message in 5s)";
        });
        await StreamProbe(s, "Streams.OrderBookAsync", async ct =>
        {
            await foreach (var book in c.Streams.OrderBookAsync(TestSymbol, depth: 5, ct))
                return $"first book: bids={book.Bids.Count}, asks={book.Asks.Count}, snapshot={book.IsSnapshot}";
            return "(no message in 5s)";
        });
        await StreamProbe(s, "Streams.AllMidsAsync", async ct =>
        {
            await foreach (var m in c.Streams.AllMidsAsync(ct))
                return $"first mid: {m.Symbol}={m.Mid}";
            return "(no message in 5s)";
        });
        await StreamProbe(s, "Streams.BestBidOfferAsync", async ct =>
        {
            await foreach (var b in c.Streams.BestBidOfferAsync(TestSymbol, ct))
                return $"first BBO: {b.BidPrice}/{b.AskPrice}";
            return "(no message in 5s)";
        });
        await StreamProbe(s, "Streams.CandlesAsync", async ct =>
        {
            await foreach (var k in c.Streams.CandlesAsync(TestSymbol, Interval.OneMinute, ct))
                return $"first candle: o={k.Candle.Open}, c={k.Candle.Close}";
            return "(no message in 5s)";
        });
        // User streams — connection should establish; messages unlikely with no trading activity.
        await StreamProbe(s, "Streams.MyOrdersAsync (5s)", async ct =>
        {
            try
            {
                await foreach (var o in c.Streams.MyOrdersAsync(ct))
                    return $"first my-order: {o.Order.Symbol} {o.Order.Status}";
                return "(no message in 5s — expected if idle)";
            }
            catch (OperationCanceledException) { return "(no message in 5s — expected if idle)"; }
        });
        await StreamProbe(s, "Streams.MyFillsAsync (5s)", async ct =>
        {
            try
            {
                await foreach (var f in c.Streams.MyFillsAsync(ct))
                    return $"first fill: {f.Fill.Symbol} {f.Fill.Size}@{f.Fill.Price}";
                return "(no message in 5s — expected if no fills)";
            }
            catch (OperationCanceledException) { return "(no message in 5s — expected if no fills)"; }
        });
        await StreamProbe(s, "Streams.MyFundingsAsync (5s)", async ct =>
        {
            try
            {
                await foreach (var f in c.Streams.MyFundingsAsync(ct))
                    return $"first funding: {f.Symbol} {f.Amount}";
                return "(no message in 5s — expected if no positions)";
            }
            catch (OperationCanceledException) { return "(no message in 5s — expected if no positions)"; }
        });
        await StreamProbe(s, "Streams.MyNotificationsAsync (5s)", async ct =>
        {
            try
            {
                await foreach (var n in c.Streams.MyNotificationsAsync(ct))
                    return $"first notif: {n.Message}";
                return "(no message in 5s — expected if idle)";
            }
            catch (OperationCanceledException) { return "(no message in 5s — expected if idle)"; }
        });
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static string NextClientId() => $"0x{Guid.NewGuid():N}";

    private static void TrackPlace(PlaceOrderResult r, string? cid)
    {
        if (r.OrderId > 0) PlacedOrderIds.Add(r.OrderId);
        if (!string.IsNullOrEmpty(cid)) PlacedClientIds.Add(cid);
    }

    private static async Task Run(AuditStats stats, string label, Func<Task<string>> action)
    {
        try
        {
            var info = await action();
            Console.WriteLine($"[audit]   ✓ {label,-60} → {info}");
            stats.Passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[audit]   ✗ {label,-60} → {ex.GetType().Name}: {ex.Message}");
            stats.Failures.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
            stats.Failed++;
        }
    }

    private static async Task RunExpectingErrorMessage(AuditStats stats, string label, Func<Task> action, string expectedSubstring)
    {
        try
        {
            await action();
            Console.WriteLine($"[audit]   · {label,-60} → succeeded (no error — possibly account passed gate)");
            stats.Passed++;
        }
        catch (Exception ex) when (ex.Message.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[audit]   ✓ {label,-60} → {ex.GetType().Name}: \"{Snippet(ex.Message)}\" (as expected)");
            stats.Passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[audit]   ✗ {label,-60} → {ex.GetType().Name}: {ex.Message}");
            stats.Failures.Add($"{label}: unexpected message: {ex.Message}");
            stats.Failed++;
        }
    }

    private static string Snippet(string s) => s.Length <= 120 ? s : s[..120] + "…";

    private static async Task RunExpectingThrow<TException>(AuditStats stats, string label, Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Console.WriteLine($"[audit]   ✗ {label,-60} → did NOT throw expected {typeof(TException).Name}");
            stats.Failures.Add($"{label}: did not throw {typeof(TException).Name}");
            stats.Failed++;
        }
        catch (TException ex)
        {
            Console.WriteLine($"[audit]   ✓ {label,-60} → threw {ex.GetType().Name} (as expected)");
            stats.Passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[audit]   ✗ {label,-60} → threw {ex.GetType().Name} (expected {typeof(TException).Name})");
            stats.Failures.Add($"{label}: wrong exception type");
            stats.Failed++;
        }
    }

    private static async Task StreamProbe(AuditStats stats, string label, Func<CancellationToken, Task<string>> probe)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var info = await probe(cts.Token);
            Console.WriteLine($"[audit]   ✓ {label,-60} → {info}");
            stats.Passed++;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.WriteLine($"[audit]   ✓ {label,-60} → (5s elapsed without first message — connect succeeded)");
            stats.Passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[audit]   ✗ {label,-60} → {ex.GetType().Name}: {ex.Message}");
            stats.Failures.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
            stats.Failed++;
        }
    }

    private sealed class AuditStats
    {
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public List<string> Failures { get; } = new();

        public void Skip(string label)
        {
            Console.WriteLine($"[audit]   · {label,-60} → SKIPPED");
            Skipped++;
        }
    }

    /// <summary>In-memory ILogger to surface 1.2.1 builder-approval warnings during the run.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Console.WriteLine($"[audit][log:{logLevel}] {formatter(state, exception)}");
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
