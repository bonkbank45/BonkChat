using System.Globalization;

namespace ChatTwo.Ai;

/// <summary>
/// Accumulates token usage reported by the providers and turns it into a
/// spend estimate, so the cost of a feature is measured rather than guessed.
/// Session totals live in memory; the monthly total is persisted and drives
/// the budget brake.
/// </summary>
public class AiUsageTracker(Plugin plugin)
{
    private Plugin Plugin { get; } = plugin;

    /// <summary> USD per million tokens (input, cached input, output). </summary>
    private static readonly Dictionary<string, (double Input, double Cached, double Output)> Pricing = new()
    {
        ["grok-4.5"] = (2.00, 0.30, 6.00),
        ["grok-4.3"] = (1.25, 0.20, 2.50),
    };

    private readonly Lock Mutex = new();

    public int Requests { get; private set; }
    public long SessionInput { get; private set; }
    public long SessionOutput { get; private set; }
    public long SessionCached { get; private set; }
    public long SessionReasoning { get; private set; }
    public double SessionCostUsd { get; private set; }

    /// <summary> True once this month's budget is used up, until overridden. </summary>
    public bool BudgetTripped { get; private set; }
    public bool BudgetOverridden { get; set; }

    public double MonthCostUsd => Plugin.Config.AiUsageMonthCostUsd;
    public double MonthCostThb => MonthCostUsd * Plugin.Config.AiUsdToThb;
    public double SessionCostThb => SessionCostUsd * Plugin.Config.AiUsdToThb;

    /// <summary> Share of input tokens served from the cache, 0-1. </summary>
    public double CachedShare => SessionInput == 0 ? 0 : (double)SessionCached / SessionInput;

    public void Record(string model, AiResponse response)
    {
        var cost = EstimateCost(model, response);

        lock (Mutex)
        {
            Requests++;
            SessionInput += response.InputTokens;
            SessionOutput += response.OutputTokens;
            SessionCached += response.CachedTokens;
            SessionReasoning += response.ReasoningTokens;
            SessionCostUsd += cost;

            RollMonthIfNeeded();
            Plugin.Config.AiUsageMonthCostUsd += cost;
            Plugin.Config.AiUsageMonthInput += response.InputTokens;
            Plugin.Config.AiUsageMonthOutput += response.OutputTokens;
        }

        Plugin.DeferredSaveFrames = 60;
        CheckBudget();
    }

    public static double EstimateCost(string model, AiResponse response)
    {
        if (!TryGetPricing(model, out var pricing))
            return 0;

        // Cached tokens are part of the input count but billed cheaper.
        var uncachedInput = Math.Max(0, response.InputTokens - response.CachedTokens);
        return (uncachedInput * pricing.Input
                + response.CachedTokens * pricing.Cached
                + response.OutputTokens * pricing.Output) / 1_000_000d;
    }

    public static bool TryGetPricing(string model, out (double Input, double Cached, double Output) pricing)
    {
        foreach (var (known, price) in Pricing)
        {
            if (model.StartsWith(known, StringComparison.OrdinalIgnoreCase))
            {
                pricing = price;
                return true;
            }
        }

        pricing = default;
        return false;
    }

    /// <summary> False when the monthly budget is spent and not overridden. </summary>
    public bool CanSpend()
    {
        RollMonthIfNeeded();
        return !BudgetTripped || BudgetOverridden;
    }

    private void CheckBudget()
    {
        var budget = Plugin.Config.AiMonthlyBudgetThb;
        if (budget <= 0)
            return;

        var spent = MonthCostThb;
        if (spent >= budget)
        {
            if (!BudgetTripped)
            {
                BudgetTripped = true;
                Plugin.Log.Information($"AI monthly budget reached: {spent:N2} / {budget:N2} THB");
            }

            return;
        }

        if (spent >= budget * 0.8 && !Plugin.Config.AiBudgetWarned)
        {
            Plugin.Config.AiBudgetWarned = true;
            Plugin.DeferredSaveFrames = 60;
            Util.WrapperUtil.AddNotification(
                $"AI spending is at {spent:N0} of {budget:N0} THB this month",
                Dalamud.Interface.ImGuiNotification.NotificationType.Warning);
        }
    }

    public void RollMonthIfNeeded()
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (Plugin.Config.AiUsageMonth == month)
            return;

        Plugin.Config.AiUsageMonth = month;
        Plugin.Config.AiUsageMonthCostUsd = 0;
        Plugin.Config.AiUsageMonthInput = 0;
        Plugin.Config.AiUsageMonthOutput = 0;
        Plugin.Config.AiBudgetWarned = false;
        BudgetTripped = false;
        BudgetOverridden = false;
        Plugin.DeferredSaveFrames = 60;
    }

    public void ResetSession()
    {
        lock (Mutex)
        {
            Requests = 0;
            SessionInput = SessionOutput = SessionCached = SessionReasoning = 0;
            SessionCostUsd = 0;
        }
    }

    public void ResetMonth()
    {
        Plugin.Config.AiUsageMonthCostUsd = 0;
        Plugin.Config.AiUsageMonthInput = 0;
        Plugin.Config.AiUsageMonthOutput = 0;
        Plugin.Config.AiBudgetWarned = false;
        BudgetTripped = false;
        BudgetOverridden = false;
        Plugin.DeferredSaveFrames = 60;
    }
}
