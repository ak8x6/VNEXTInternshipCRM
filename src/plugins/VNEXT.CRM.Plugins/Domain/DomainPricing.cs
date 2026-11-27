using System;
using System.Collections.Generic;
using VNEXT.CRM.Plugins.Core;

namespace VNEXT.CRM.Plugins.Domain
{
    public sealed class LinePricingResult
    {
        public decimal GrossAmount { get; }
        public decimal DiscountAmount { get; }
        public decimal NetAmount { get; }

        public LinePricingResult(decimal grossAmount, decimal discountAmount, decimal netAmount)
        {
            GrossAmount = grossAmount;
            DiscountAmount = discountAmount;
            NetAmount = netAmount;
        }
    }

    public sealed class LineAggregateItem
    {
        public decimal NetAmount { get; }
        public decimal DiscountPercent { get; }

        public LineAggregateItem(decimal netAmount, decimal discountPercent)
        {
            NetAmount = netAmount;
            DiscountPercent = discountPercent;
        }
    }

    public sealed class OpportunityTotalsResult
    {
        public decimal Total { get; }
        public decimal MaxDiscount { get; }
        public int LineCount { get; }

        public OpportunityTotalsResult(decimal total, decimal maxDiscount, int lineCount)
        {
            Total = total;
            MaxDiscount = maxDiscount;
            LineCount = lineCount;
        }
    }

    public static class DomainPricing
    {
        public const decimal MaxBudget = 1_000_000_000m;
        public const decimal MaxUnitPrice = 1_000_000m;
        public const decimal MaxQuantity = 10_000m;
        public const decimal MaxAmount = 1_000_000_000m;

        public const int BudgetDecimalPlaces = 2;
        public const int UnitPriceDecimalPlaces = 4;
        public const int QuantityDecimalPlaces = 3;
        public const int DiscountDecimalPlaces = 2;
        public const int MoneyDecimalPlaces = 2;

        public static bool HasAtMostDecimalPlaces(decimal value, int maxPlaces)
        {
            if (maxPlaces < 0)
            {
                return false;
            }

            return Math.Round(value, maxPlaces, MidpointRounding.AwayFromZero) == value;
        }

        public static void ValidateBaseCurrency(Guid? currencyId, CrmPolicyConfig config, Guid correlationId)
        {
            if (config == null)
            {
                CrmError.Throw(CrmError.CRM008, "CRM policy configuration is unavailable.", correlationId);
            }

            if (currencyId.HasValue && currencyId.Value != Guid.Empty && currencyId.Value != config.BaseCurrencyId)
            {
                CrmError.Throw(
                    CrmError.CRM008,
                    "Currency must match the environment base currency.",
                    correlationId);
            }
        }

        public static void ValidateOptionalBudget(decimal? budget, Guid? currencyId, CrmPolicyConfig config, Guid correlationId)
        {
            ValidateBaseCurrency(currencyId, config, correlationId);

            if (!budget.HasValue)
            {
                return;
            }

            var value = budget.Value;
            if (value < 0m || value > MaxBudget || !HasAtMostDecimalPlaces(value, BudgetDecimalPlaces))
            {
                CrmError.Throw(
                    CrmError.CRM001,
                    "Budget must be between 0 and 1,000,000,000 with at most 2 decimal places.",
                    correlationId);
            }
        }

        public static void ValidateProductUnitPrice(decimal unitPrice, CrmPolicyConfig config, Guid correlationId)
        {
            if (config == null)
            {
                CrmError.Throw(CrmError.CRM008, "CRM policy configuration is unavailable.", correlationId);
            }

            if (unitPrice <= 0m || unitPrice > MaxUnitPrice || !HasAtMostDecimalPlaces(unitPrice, UnitPriceDecimalPlaces))
            {
                CrmError.Throw(
                    CrmError.CRM008,
                    "Product unit price must be greater than 0, at most 1,000,000, and have at most 4 decimal places.",
                    correlationId);
            }
        }

        public static LinePricingResult CalculateLine(
            decimal quantity,
            decimal snapshotUnitPrice,
            decimal discountPercent,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (config == null)
            {
                CrmError.Throw(CrmError.CRM008, "CRM policy configuration is unavailable.", correlationId);
            }

            if (quantity <= 0m || quantity > MaxQuantity || !HasAtMostDecimalPlaces(quantity, QuantityDecimalPlaces))
            {
                CrmError.Throw(
                    CrmError.CRM008,
                    "Quantity must be greater than 0, at most 10,000, and have at most 3 decimal places.",
                    correlationId);
            }

            if (discountPercent < 0m || discountPercent > config.MaxDiscount || !HasAtMostDecimalPlaces(discountPercent, DiscountDecimalPlaces))
            {
                CrmError.Throw(
                    CrmError.CRM008,
                    string.Format(
                        "Discount percent must be between 0 and {0:0.##} with at most 2 decimal places.",
                        config.MaxDiscount),
                    correlationId);
            }

            ValidateProductUnitPrice(snapshotUnitPrice, config, correlationId);

            try
            {
                checked
                {
                    var gross = Math.Round(quantity * snapshotUnitPrice, MoneyDecimalPlaces, MidpointRounding.AwayFromZero);
                    var discountAmount = Math.Round(gross * discountPercent / 100m, MoneyDecimalPlaces, MidpointRounding.AwayFromZero);
                    var net = gross - discountAmount;

                    if (gross < 0m || discountAmount < 0m || net < 0m || net > MaxAmount)
                    {
                        CrmError.Throw(
                            CrmError.CRM009,
                            "Calculated line amount is out of supported bounds.",
                            correlationId);
                    }

                    return new LinePricingResult(gross, discountAmount, net);
                }
            }
            catch (OverflowException)
            {
                CrmError.Throw(
                    CrmError.CRM009,
                    "Line pricing calculation exceeded numeric bounds.",
                    correlationId);
                return null;
            }
        }

        public static OpportunityTotalsResult CalculateOpportunityTotals(
            IEnumerable<LineAggregateItem> lines,
            Guid correlationId)
        {
            if (lines == null)
            {
                return new OpportunityTotalsResult(0.00m, 0.00m, 0);
            }

            decimal total = 0.00m;
            decimal maxDiscount = 0.00m;
            int lineCount = 0;

            try
            {
                checked
                {
                    foreach (var line in lines)
                    {
                        if (line == null)
                        {
                            CrmError.Throw(
                                CrmError.CRM009,
                                "Opportunity line aggregate item cannot be null.",
                                correlationId);
                        }

                        if (line.NetAmount < 0m || line.DiscountPercent < 0m)
                        {
                            CrmError.Throw(
                                CrmError.CRM009,
                                "Opportunity line net amount and discount cannot be negative.",
                                correlationId);
                        }

                        total += line.NetAmount;
                        if (lineCount == 0 || line.DiscountPercent > maxDiscount)
                        {
                            maxDiscount = line.DiscountPercent;
                        }

                        lineCount++;
                    }
                }
            }
            catch (OverflowException)
            {
                CrmError.Throw(
                    CrmError.CRM009,
                    "Opportunity total calculation exceeded numeric bounds.",
                    correlationId);
            }

            if (lineCount == 0)
            {
                return new OpportunityTotalsResult(0.00m, 0.00m, 0);
            }

            total = Math.Round(total, MoneyDecimalPlaces, MidpointRounding.AwayFromZero);
            maxDiscount = Math.Round(maxDiscount, DiscountDecimalPlaces, MidpointRounding.AwayFromZero);

            if (total < 0m || total > MaxAmount)
            {
                CrmError.Throw(
                    CrmError.CRM009,
                    "Opportunity total exceeds the maximum allowed amount of 1,000,000,000.",
                    correlationId);
            }

            return new OpportunityTotalsResult(total, maxDiscount, lineCount);
        }

        public static bool RequiresApproval(decimal total, decimal maxDiscount, CrmPolicyConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            return total >= config.ApprovalAmount || maxDiscount > config.ApprovalDiscount;
        }
    }
}
