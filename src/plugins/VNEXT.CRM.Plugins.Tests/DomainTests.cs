using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Domain;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    public class DomainTests
    {
        private static readonly Guid BaseCurrencyId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ForeignCurrencyId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid CorrelationId = new Guid("33333333-3333-3333-3333-333333333333");
        private static readonly CrmPolicyConfig DefaultConfig = CrmPolicyConfig.CreateDefaults(BaseCurrencyId);

        private static void AssertCrmError(string expectedCode, Action action)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(action);
            Assert.StartsWith("[" + expectedCode + "] ", ex.Message);
            Assert.Contains("(CorrelationId: " + CorrelationId.ToString("D") + ")", ex.Message);
        }

        [Fact]
        public void DomainPricing_CalculateLine_CanonicalSpecExample_ReturnsExactAmounts()
        {
            var result = DomainPricing.CalculateLine(3m, 100.0050m, 10m, DefaultConfig, CorrelationId);

            Assert.Equal(300.02m, result.GrossAmount);
            Assert.Equal(30.00m, result.DiscountAmount);
            Assert.Equal(270.02m, result.NetAmount);
        }

        [Fact]
        public void DomainPricing_CalculateLine_UsesMidpointAwayFromZeroRounding()
        {
            // Even-digit midpoint: 1.005 -> 1.01 (away from zero, not 1.00 banker's rounding)
            var evenMidpoint = DomainPricing.CalculateLine(1m, 1.005m, 0m, DefaultConfig, CorrelationId);
            Assert.Equal(1.01m, evenMidpoint.GrossAmount);
            Assert.Equal(0.00m, evenMidpoint.DiscountAmount);
            Assert.Equal(1.01m, evenMidpoint.NetAmount);

            // Odd-digit midpoint: 1.015 -> 1.02
            var oddMidpoint = DomainPricing.CalculateLine(1m, 1.015m, 0m, DefaultConfig, CorrelationId);
            Assert.Equal(1.02m, oddMidpoint.GrossAmount);

            // Discount midpoint: 10.10 * 5% = 0.505 -> 0.51; Net = 10.10 - 0.51 = 9.59
            var discountMidpoint = DomainPricing.CalculateLine(1m, 10.10m, 5m, DefaultConfig, CorrelationId);
            Assert.Equal(10.10m, discountMidpoint.GrossAmount);
            Assert.Equal(0.51m, discountMidpoint.DiscountAmount);
            Assert.Equal(9.59m, discountMidpoint.NetAmount);
        }

        [Fact]
        public void DomainPricing_PrecisionLimits_AcceptsTrailingZerosAndRejectsExtraSignificantDigits()
        {
            // Trailing zeros beyond scale are numerically exact and accepted
            var accepted = DomainPricing.CalculateLine(3.12500m, 100.005000m, 10.2500m, DefaultConfig, CorrelationId);
            Assert.Equal(312.52m, accepted.GrossAmount);

            // Extra significant digits rejected with CRM008
            AssertCrmError(CrmError.CRM008, () => DomainPricing.CalculateLine(1.0001m, 10m, 0m, DefaultConfig, CorrelationId));
            AssertCrmError(CrmError.CRM008, () => DomainPricing.CalculateLine(1m, 10.00001m, 0m, DefaultConfig, CorrelationId));
            AssertCrmError(CrmError.CRM008, () => DomainPricing.CalculateLine(1m, 10m, 5.001m, DefaultConfig, CorrelationId));

            // Optional budget precision (2 places)
            DomainPricing.ValidateOptionalBudget(1234.5600m, BaseCurrencyId, DefaultConfig, CorrelationId);
            AssertCrmError(CrmError.CRM001, () => DomainPricing.ValidateOptionalBudget(1234.567m, BaseCurrencyId, DefaultConfig, CorrelationId));
        }

        [Theory]
        [InlineData("0", "100", "0")]
        [InlineData("-1", "100", "0")]
        [InlineData("10000.001", "100", "0")]
        [InlineData("1", "0", "0")]
        [InlineData("1", "-5", "0")]
        [InlineData("1", "1000000.0001", "0")]
        [InlineData("1", "100", "-0.01")]
        [InlineData("1", "100", "30.01")]
        public void DomainPricing_CalculateLine_RejectsOutOfRangeInputsWithCrm008(
            string qty,
            string price,
            string discount)
        {
            AssertCrmError(
                CrmError.CRM008,
                () => DomainPricing.CalculateLine(
                    decimal.Parse(qty, CultureInfo.InvariantCulture),
                    decimal.Parse(price, CultureInfo.InvariantCulture),
                    decimal.Parse(discount, CultureInfo.InvariantCulture),
                    DefaultConfig,
                    CorrelationId));
        }

        [Fact]
        public void DomainPricing_CalculateLine_RejectsNetExceedingOneBillionWithCrm009()
        {
            // 10,000 * 1,000,000 = 10,000,000,000 > 1,000,000,000
            AssertCrmError(
                CrmError.CRM009,
                () => DomainPricing.CalculateLine(10_000m, 1_000_000m, 0m, DefaultConfig, CorrelationId));
        }

        [Fact]
        public void DomainPricing_CalculateOpportunityTotals_HandlesEmptyMultipleAndOverflow()
        {
            var emptyResult = DomainPricing.CalculateOpportunityTotals(Array.Empty<LineAggregateItem>(), CorrelationId);
            Assert.Equal(0.00m, emptyResult.Total);
            Assert.Equal(0.00m, emptyResult.MaxDiscount);
            Assert.Equal(0, emptyResult.LineCount);

            var nullResult = DomainPricing.CalculateOpportunityTotals(null, CorrelationId);
            Assert.Equal(0.00m, nullResult.Total);
            Assert.Equal(0.00m, nullResult.MaxDiscount);
            Assert.Equal(0, nullResult.LineCount);

            var validLines = new[]
            {
                new LineAggregateItem(270.02m, 10.00m),
                new LineAggregateItem(500.00m, 15.50m)
            };
            var totals = DomainPricing.CalculateOpportunityTotals(validLines, CorrelationId);
            Assert.Equal(770.02m, totals.Total);
            Assert.Equal(15.50m, totals.MaxDiscount);
            Assert.Equal(2, totals.LineCount);

            // Total > 1,000,000,000 -> CRM009
            var overLimitLines = new[]
            {
                new LineAggregateItem(600_000_000.00m, 0m),
                new LineAggregateItem(400_000_000.01m, 0m)
            };
            AssertCrmError(
                CrmError.CRM009,
                () => DomainPricing.CalculateOpportunityTotals(overLimitLines, CorrelationId));

            // Decimal overflow -> CRM009
            var overflowLines = new[]
            {
                new LineAggregateItem(decimal.MaxValue, 0m),
                new LineAggregateItem(1m, 0m)
            };
            AssertCrmError(
                CrmError.CRM009,
                () => DomainPricing.CalculateOpportunityTotals(overflowLines, CorrelationId));
        }

        [Theory]
        [InlineData("49999.99", "10.00", false)]
        [InlineData("50000.00", "0.00", true)]
        [InlineData("100.00", "10.00", false)]
        [InlineData("100.00", "10.01", true)]
        public void DomainPricing_RequiresApproval_MatchesExactBr20Boundaries(
            string total,
            string maxDiscount,
            bool expected)
        {
            var actual = DomainPricing.RequiresApproval(
                decimal.Parse(total, CultureInfo.InvariantCulture),
                decimal.Parse(maxDiscount, CultureInfo.InvariantCulture),
                DefaultConfig);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DomainValidators_ResolveCustomerSelectionPath_EnforcesFullMatrixAndRejectsEmptyGuids()
        {
            var accountId = Guid.NewGuid();
            var contactId = Guid.NewGuid();

            Assert.Equal(
                CustomerSelectionPath.ReuseAccountAndContact,
                DomainValidators.ResolveCustomerSelectionPath(accountId, contactId, CorrelationId));

            Assert.Equal(
                CustomerSelectionPath.ReuseAccountCreateContact,
                DomainValidators.ResolveCustomerSelectionPath(accountId, null, CorrelationId));

            Assert.Equal(
                CustomerSelectionPath.CreateAccountAndContact,
                DomainValidators.ResolveCustomerSelectionPath(null, null, CorrelationId));

            AssertCrmError(
                CrmError.CRM004,
                () => DomainValidators.ResolveCustomerSelectionPath(null, contactId, CorrelationId));

            AssertCrmError(
                CrmError.CRM003,
                () => DomainValidators.ResolveCustomerSelectionPath(Guid.Empty, null, CorrelationId));

            AssertCrmError(
                CrmError.CRM003,
                () => DomainValidators.ResolveCustomerSelectionPath(accountId, Guid.Empty, CorrelationId));
        }

        [Fact]
        public void DomainValidators_ValidateOpenLead_EnforcesBr01Br02AndBaseCurrency()
        {
            var pastFollowUp = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc);

            // Valid New Lead (past follow-up permitted; blank nextStep allowed while New)
            DomainValidators.ValidateOpenLead(
                "ERP Modernization",
                "Al-Mansoor",
                CrmSchema.Lead.SourceValues.Referral,
                CrmSchema.Lead.RatingValues.Warm,
                "buyer@contoso.com",
                null,
                pastFollowUp,
                25000m,
                BaseCurrencyId,
                CrmSchema.Lead.Status.New,
                null,
                DefaultConfig,
                CorrelationId);

            // Valid Contacted Lead with nonblank nextStep
            DomainValidators.ValidateOpenLead(
                "ERP Modernization",
                "Al-Mansoor",
                CrmSchema.Lead.SourceValues.Website,
                CrmSchema.Lead.RatingValues.Hot,
                null,
                "+1-555-0100",
                pastFollowUp,
                null,
                null,
                CrmSchema.Lead.Status.Contacted,
                "Schedule discovery workshop",
                DefaultConfig,
                CorrelationId);

            // Contacted without nextStep -> CRM001
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateOpenLead(
                "ERP Modernization",
                "Al-Mansoor",
                CrmSchema.Lead.SourceValues.Referral,
                CrmSchema.Lead.RatingValues.Warm,
                "buyer@contoso.com",
                null,
                pastFollowUp,
                null,
                BaseCurrencyId,
                CrmSchema.Lead.Status.Contacted,
                "   ",
                DefaultConfig,
                CorrelationId));

            // Missing email and phone -> CRM001
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateOpenLead(
                "ERP Modernization",
                "Al-Mansoor",
                CrmSchema.Lead.SourceValues.Referral,
                CrmSchema.Lead.RatingValues.Warm,
                " ",
                null,
                pastFollowUp,
                null,
                BaseCurrencyId,
                CrmSchema.Lead.Status.New,
                null,
                DefaultConfig,
                CorrelationId));

            // Foreign currency -> CRM008
            AssertCrmError(CrmError.CRM008, () => DomainValidators.ValidateOpenLead(
                "ERP Modernization",
                "Al-Mansoor",
                CrmSchema.Lead.SourceValues.Referral,
                CrmSchema.Lead.RatingValues.Warm,
                "buyer@contoso.com",
                null,
                pastFollowUp,
                1000m,
                ForeignCurrencyId,
                CrmSchema.Lead.Status.New,
                null,
                DefaultConfig,
                CorrelationId));
        }

        [Fact]
        public void DomainValidators_ValidateLeadDisqualification_EnforcesDuplicateAndCommentRules()
        {
            var leadId = Guid.NewGuid();
            var otherLeadId = Guid.NewGuid();

            DomainValidators.ValidateLeadDisqualification(
                leadId,
                CrmSchema.Lead.DisqualificationReasonValues.NoBudget,
                "No fiscal budget approved this year.",
                null,
                CorrelationId);

            DomainValidators.ValidateLeadDisqualification(
                leadId,
                CrmSchema.Lead.DisqualificationReasonValues.Duplicate,
                "Duplicate of existing active lead.",
                otherLeadId,
                CorrelationId);

            // Duplicate referencing self -> CRM001
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateLeadDisqualification(
                leadId,
                CrmSchema.Lead.DisqualificationReasonValues.Duplicate,
                "Self duplicate attempt",
                leadId,
                CorrelationId));

            // Duplicate without duplicateOf -> CRM001
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateLeadDisqualification(
                leadId,
                CrmSchema.Lead.DisqualificationReasonValues.Duplicate,
                "Missing target",
                null,
                CorrelationId));

            // Non-Duplicate with duplicateOf supplied -> CRM001
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateLeadDisqualification(
                leadId,
                CrmSchema.Lead.DisqualificationReasonValues.NoFit,
                "Out of target vertical",
                otherLeadId,
                CorrelationId));

            // Blank comments -> CRM001
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateLeadDisqualification(
                leadId,
                CrmSchema.Lead.DisqualificationReasonValues.NoResponse,
                "   ",
                null,
                CorrelationId));
        }

        [Fact]
        public void DomainValidators_ValidateLeadQualificationReadiness_EnforcesBr03AndUtcTodayBound()
        {
            var utcNow = new DateTime(2026, 11, 23, 14, 30, 0, DateTimeKind.Utc);
            var today = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
            var yesterday = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);

            // Valid Active/Contacted Lead with ExpectedCloseDate == UTC Today
            DomainValidators.ValidateLeadQualificationReadiness(
                CrmSchema.Lead.State.Active,
                CrmSchema.Lead.Status.Contacted,
                "Acme Industrial",
                "Hassan",
                "hassan@acme.example",
                null,
                "Replace legacy order tracking",
                "Run technical scoping call",
                yesterday,
                75000m,
                BaseCurrencyId,
                "Acme Industrial — Core CRM Rollout",
                today,
                utcNow,
                DefaultConfig,
                CorrelationId);

            // ExpectedCloseDate < UTC Today -> CRM002
            AssertCrmError(CrmError.CRM002, () => DomainValidators.ValidateLeadQualificationReadiness(
                CrmSchema.Lead.State.Active,
                CrmSchema.Lead.Status.Contacted,
                "Acme Industrial",
                "Hassan",
                "hassan@acme.example",
                null,
                "Replace legacy order tracking",
                "Run technical scoping call",
                today,
                75000m,
                BaseCurrencyId,
                "Acme Industrial — Core CRM Rollout",
                yesterday,
                utcNow,
                DefaultConfig,
                CorrelationId));

            // Lead status still New -> CRM002
            AssertCrmError(CrmError.CRM002, () => DomainValidators.ValidateLeadQualificationReadiness(
                CrmSchema.Lead.State.Active,
                CrmSchema.Lead.Status.New,
                "Acme Industrial",
                "Hassan",
                "hassan@acme.example",
                null,
                "Replace legacy order tracking",
                "Run technical scoping call",
                today,
                null,
                BaseCurrencyId,
                "Acme Industrial — Core CRM Rollout",
                today,
                utcNow,
                DefaultConfig,
                CorrelationId));

            // Foreign currency -> CRM008
            AssertCrmError(CrmError.CRM008, () => DomainValidators.ValidateLeadQualificationReadiness(
                CrmSchema.Lead.State.Active,
                CrmSchema.Lead.Status.Contacted,
                "Acme Industrial",
                "Hassan",
                "hassan@acme.example",
                null,
                "Replace legacy order tracking",
                "Run technical scoping call",
                today,
                1000m,
                ForeignCurrencyId,
                "Acme Industrial — Core CRM Rollout",
                today,
                utcNow,
                DefaultConfig,
                CorrelationId));
        }

        [Fact]
        public void DomainValidators_AccountContactAndProduct_EnforceIntegrityRules()
        {
            // Account
            DomainValidators.ValidateAccount(
                "Northwind Manufacturing",
                CrmSchema.Account.AccountTypeValues.Prospect,
                CrmSchema.Account.IndustryValues.Manufacturing,
                CorrelationId);
            DomainValidators.ValidateAccount(
                "Northwind Manufacturing",
                CrmSchema.Account.AccountTypeValues.Customer,
                null,
                CorrelationId);
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateAccount(" ", CrmSchema.Account.AccountTypeValues.Prospect, null, CorrelationId));
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateAccount("Northwind", 999, null, CorrelationId));
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateAccount("Northwind", CrmSchema.Account.AccountTypeValues.Partner, 999, CorrelationId));

            // Contact
            var accountId = Guid.NewGuid();
            DomainValidators.ValidateContact(
                "Youssef",
                CrmSchema.Entities.Account,
                accountId,
                "youssef@northwind.example",
                null,
                CrmSchema.Contact.PreferredMethodValues.Email,
                CorrelationId);

            Assert.Equal(CrmSchema.Contact.PreferredMethodValues.Email, DomainValidators.ResolveDefaultPreferredMethod("a@b.co", "+1555"));
            Assert.Equal(CrmSchema.Contact.PreferredMethodValues.Phone, DomainValidators.ResolveDefaultPreferredMethod(" ", "+1555"));

            // Contact parent must be Account -> CRM004
            AssertCrmError(CrmError.CRM004, () => DomainValidators.ValidateContact(
                "Youssef",
                CrmSchema.Entities.Contact,
                accountId,
                "youssef@northwind.example",
                null,
                CrmSchema.Contact.PreferredMethodValues.Email,
                CorrelationId));
            AssertCrmError(CrmError.CRM004, () => DomainValidators.ValidateContact(
                "Youssef",
                CrmSchema.Entities.Account,
                Guid.Empty,
                "youssef@northwind.example",
                null,
                CrmSchema.Contact.PreferredMethodValues.Email,
                CorrelationId));

            // Preferred method mismatch -> CRM001
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateContact(
                "Youssef",
                CrmSchema.Entities.Account,
                accountId,
                null,
                "+1-555-0199",
                CrmSchema.Contact.PreferredMethodValues.Email,
                CorrelationId));
            AssertCrmError(CrmError.CRM001, () => DomainValidators.ValidateContact(
                "Youssef",
                CrmSchema.Entities.Account,
                accountId,
                "youssef@northwind.example",
                null,
                CrmSchema.Contact.PreferredMethodValues.Phone,
                CorrelationId));

            // Product & SKU rules
            Assert.True(DomainValidators.IsValidSku("CRM-ENT-2026"));
            Assert.False(DomainValidators.IsValidSku("crm-ent-2026"));
            Assert.False(DomainValidators.IsValidSku(" CRM-ENT-2026"));
            Assert.False(DomainValidators.IsValidSku("CRM_ENT"));
            Assert.False(DomainValidators.IsValidSku(new string('A', 51)));

            DomainValidators.ValidateProduct(
                "Enterprise CRM License",
                "CRM-ENT-01",
                "Annual enterprise seat subscription.",
                1200.5000m,
                BaseCurrencyId,
                DefaultConfig,
                CorrelationId);

            AssertCrmError(CrmError.CRM008, () => DomainValidators.ValidateProduct(
                "Enterprise CRM License",
                "bad_sku",
                "Annual enterprise seat subscription.",
                1200m,
                BaseCurrencyId,
                DefaultConfig,
                CorrelationId));

            AssertCrmError(CrmError.CRM008, () => DomainValidators.ValidateProduct(
                "Enterprise CRM License",
                "CRM-ENT-01",
                " ",
                1200m,
                BaseCurrencyId,
                DefaultConfig,
                CorrelationId));

            AssertCrmError(CrmError.CRM008, () => DomainValidators.ValidateProduct(
                "Enterprise CRM License",
                "CRM-ENT-01",
                "Annual enterprise seat subscription.",
                1200m,
                ForeignCurrencyId,
                DefaultConfig,
                CorrelationId));
        }

        [Fact]
        public void OpportunityLifecycleRules_StageProgressionProbabilitiesAndBpfPaths_MatchSpec()
        {
            Assert.Equal(10, OpportunityLifecycleRules.GetProbabilityForOpenStage(CrmSchema.Opportunity.StageValues.Qualify, CorrelationId));
            Assert.Equal(30, OpportunityLifecycleRules.GetProbabilityForOpenStage(CrmSchema.Opportunity.StageValues.Develop, CorrelationId));
            Assert.Equal(60, OpportunityLifecycleRules.GetProbabilityForOpenStage(CrmSchema.Opportunity.StageValues.Propose, CorrelationId));
            Assert.Equal(90, OpportunityLifecycleRules.GetProbabilityForOpenStage(CrmSchema.Opportunity.StageValues.Close, CorrelationId));
            AssertCrmError(CrmError.CRM006, () => OpportunityLifecycleRules.GetProbabilityForOpenStage(999, CorrelationId));

            Assert.Equal(CrmSchema.Opportunity.StageValues.Develop, OpportunityLifecycleRules.GetNextAdjacentStage(CrmSchema.Opportunity.StageValues.Qualify, null, CorrelationId));
            Assert.Equal(CrmSchema.Opportunity.StageValues.Propose, OpportunityLifecycleRules.GetNextAdjacentStage(CrmSchema.Opportunity.StageValues.Develop, CrmSchema.Opportunity.StageValues.Propose, CorrelationId));
            Assert.Equal(CrmSchema.Opportunity.StageValues.Close, OpportunityLifecycleRules.GetNextAdjacentStage(CrmSchema.Opportunity.StageValues.Propose, CrmSchema.Opportunity.StageValues.Close, CorrelationId));

            // Skip, backward, from-Close, unknown -> CRM006
            AssertCrmError(CrmError.CRM006, () => OpportunityLifecycleRules.GetNextAdjacentStage(CrmSchema.Opportunity.StageValues.Qualify, CrmSchema.Opportunity.StageValues.Propose, CorrelationId));
            AssertCrmError(CrmError.CRM006, () => OpportunityLifecycleRules.GetNextAdjacentStage(CrmSchema.Opportunity.StageValues.Propose, CrmSchema.Opportunity.StageValues.Develop, CorrelationId));
            AssertCrmError(CrmError.CRM006, () => OpportunityLifecycleRules.GetNextAdjacentStage(CrmSchema.Opportunity.StageValues.Close, null, CorrelationId));
            AssertCrmError(CrmError.CRM006, () => OpportunityLifecycleRules.GetNextAdjacentStage(999, null, CorrelationId));

            // BPF stage GUIDs & traversedpath
            Assert.Equal(CrmSchema.Bpf.StageQualifyId, OpportunityLifecycleRules.GetBpfStageIdForOpportunityStage(CrmSchema.Opportunity.StageValues.Qualify, CorrelationId));
            Assert.Equal(CrmSchema.Bpf.StageCloseId, OpportunityLifecycleRules.GetBpfStageIdForOpportunityStage(CrmSchema.Opportunity.StageValues.Close, CorrelationId));

            var expectedClosePath = string.Join(
                ",",
                CrmSchema.Bpf.StageQualifyId.ToString("D"),
                CrmSchema.Bpf.StageDevelopId.ToString("D"),
                CrmSchema.Bpf.StageProposeId.ToString("D"),
                CrmSchema.Bpf.StageCloseId.ToString("D"));
            Assert.Equal(expectedClosePath, OpportunityLifecycleRules.BuildTraversedPathForStage(CrmSchema.Opportunity.StageValues.Close, CorrelationId));
            Assert.Equal(CrmSchema.Bpf.StageQualifyId.ToString("D"), OpportunityLifecycleRules.BuildTraversedPathForStage(CrmSchema.Opportunity.StageValues.Qualify, CorrelationId));
        }

        [Fact]
        public void OpportunityLifecycleRules_Br12ReadinessAndPostProgressionClearing_EnforceCrm007()
        {
            var expectedClose = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
            var followUp = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

            // Valid Develop / Propose / Close advance readiness
            OpportunityLifecycleRules.ValidateStageAdvanceReadiness(
                CrmSchema.Opportunity.StageValues.Develop,
                "Consolidate regional pipeline",
                "VP of Sales",
                true,
                "Prepare commercial quote",
                0,
                0m,
                null,
                null,
                CorrelationId);

            OpportunityLifecycleRules.ValidateStageAdvanceReadiness(
                CrmSchema.Opportunity.StageValues.Propose,
                "Consolidate regional pipeline",
                "VP of Sales",
                true,
                "Review proposal with procurement",
                1,
                15000m,
                "3-year SaaS subscription with onboarding",
                null,
                CorrelationId);

            OpportunityLifecycleRules.ValidateStageAdvanceReadiness(
                CrmSchema.Opportunity.StageValues.Close,
                "Consolidate regional pipeline",
                "VP of Sales",
                true,
                "Obtain final executive signature",
                2,
                32000m,
                "3-year SaaS subscription with onboarding",
                expectedClose,
                CorrelationId);

            // Missing readiness prerequisites -> CRM007
            AssertCrmError(CrmError.CRM007, () => OpportunityLifecycleRules.ValidateStageAdvanceReadiness(
                CrmSchema.Opportunity.StageValues.Develop,
                "Need",
                " ",
                true,
                "Next step",
                0,
                0m,
                null,
                null,
                CorrelationId));

            AssertCrmError(CrmError.CRM007, () => OpportunityLifecycleRules.ValidateStageAdvanceReadiness(
                CrmSchema.Opportunity.StageValues.Propose,
                "Need",
                "CFO",
                true,
                "Next step",
                0,
                0m,
                "Summary",
                null,
                CorrelationId));

            AssertCrmError(CrmError.CRM007, () => OpportunityLifecycleRules.ValidateStageAdvanceReadiness(
                CrmSchema.Opportunity.StageValues.Close,
                "Need",
                "CFO",
                true,
                "Next step",
                1,
                5000m,
                "Summary",
                null,
                CorrelationId));

            // Post-progression clearing rejected -> CRM007
            OpportunityLifecycleRules.ValidatePersistedOpenStageFields(
                CrmSchema.Opportunity.StageValues.Qualify,
                "Deal A",
                null,
                null,
                null,
                "Follow up",
                expectedClose,
                followUp,
                CorrelationId);

            AssertCrmError(CrmError.CRM007, () => OpportunityLifecycleRules.ValidatePersistedOpenStageFields(
                CrmSchema.Opportunity.StageValues.Develop,
                "Deal A",
                "Need",
                " ",
                null,
                "Follow up",
                expectedClose,
                followUp,
                CorrelationId));

            AssertCrmError(CrmError.CRM007, () => OpportunityLifecycleRules.ValidatePersistedOpenStageFields(
                CrmSchema.Opportunity.StageValues.Propose,
                "Deal A",
                "Need",
                "CFO",
                null,
                "Follow up",
                expectedClose,
                followUp,
                CorrelationId));
        }

        [Fact]
        public void OpportunityLifecycleRules_WonAndLostEligibility_EnforceStageLinesReasonAndDateBounds()
        {
            var createdOnUtc = new DateTime(2026, 11, 10, 18, 45, 0, DateTimeKind.Utc);
            var utcNow = new DateTime(2026, 11, 23, 9, 15, 0, DateTimeKind.Utc);
            var createdDate = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
            var today = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
            var dayBeforeCreated = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
            var tomorrow = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);

            // Won valid on both inclusive bounds
            OpportunityLifecycleRules.ValidateWonEligibility(
                CrmSchema.Opportunity.State.Active,
                CrmSchema.Opportunity.Status.Open,
                CrmSchema.Opportunity.StageValues.Close,
                1,
                12500m,
                createdDate,
                createdOnUtc,
                utcNow,
                "Signed order form received.",
                CorrelationId);

            OpportunityLifecycleRules.ValidateWonEligibility(
                CrmSchema.Opportunity.State.Active,
                CrmSchema.Opportunity.Status.Open,
                CrmSchema.Opportunity.StageValues.Close,
                1,
                12500m,
                today,
                createdOnUtc,
                utcNow,
                "Signed order form received.",
                CorrelationId);

            // Won date out of range -> CRM014
            AssertCrmError(CrmError.CRM014, () => OpportunityLifecycleRules.ValidateWonEligibility(
                CrmSchema.Opportunity.State.Active,
                CrmSchema.Opportunity.Status.Open,
                CrmSchema.Opportunity.StageValues.Close,
                1,
                12500m,
                dayBeforeCreated,
                createdOnUtc,
                utcNow,
                "Signed",
                CorrelationId));

            AssertCrmError(CrmError.CRM014, () => OpportunityLifecycleRules.ValidateWonEligibility(
                CrmSchema.Opportunity.State.Active,
                CrmSchema.Opportunity.Status.Open,
                CrmSchema.Opportunity.StageValues.Close,
                1,
                12500m,
                tomorrow,
                createdOnUtc,
                utcNow,
                "Signed",
                CorrelationId));

            // Won not in Close or zero lines -> CRM014
            AssertCrmError(CrmError.CRM014, () => OpportunityLifecycleRules.ValidateWonEligibility(
                CrmSchema.Opportunity.State.Active,
                CrmSchema.Opportunity.Status.Open,
                CrmSchema.Opportunity.StageValues.Propose,
                1,
                12500m,
                today,
                createdOnUtc,
                utcNow,
                "Signed",
                CorrelationId));

            AssertCrmError(CrmError.CRM014, () => OpportunityLifecycleRules.ValidateWonEligibility(
                CrmSchema.Opportunity.State.Active,
                CrmSchema.Opportunity.Status.Open,
                CrmSchema.Opportunity.StageValues.Close,
                0,
                0m,
                today,
                createdOnUtc,
                utcNow,
                "Signed",
                CorrelationId));

            // Lost valid from any stage with zero lines
            OpportunityLifecycleRules.ValidateLostEligibility(
                CrmSchema.Opportunity.State.Active,
                CrmSchema.Opportunity.Status.Open,
                CrmSchema.Opportunity.LossReasonValues.Other,
                today,
                createdOnUtc,
                utcNow,
                "Project deferred indefinitely.",
                CorrelationId);

            // Lost missing reason or blank comments -> CRM014
            AssertCrmError(CrmError.CRM014, () => OpportunityLifecycleRules.ValidateLostEligibility(
                CrmSchema.Opportunity.State.Active,
                CrmSchema.Opportunity.Status.Open,
                null,
                today,
                createdOnUtc,
                utcNow,
                "Customer cancelled.",
                CorrelationId));

            AssertCrmError(CrmError.CRM014, () => OpportunityLifecycleRules.ValidateLostEligibility(
                CrmSchema.Opportunity.State.Active,
                CrmSchema.Opportunity.Status.Open,
                CrmSchema.Opportunity.LossReasonValues.Price,
                today,
                createdOnUtc,
                utcNow,
                "   ",
                CorrelationId));
        }

        [Fact]
        public void OpportunityLifecycleRules_EvaluateDealRisk_MatchesExactBr30Boundaries()
        {
            var asOf = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
            var today = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
            var yesterday = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);

            // Closed deal -> Closed (all flags false even if dates are overdue)
            var closed = OpportunityLifecycleRules.EvaluateDealRisk(
                false,
                yesterday,
                asOf.AddDays(-30),
                yesterday,
                asOf,
                DefaultConfig);
            Assert.Equal(DealHealthStatus.Closed, closed.Status);
            Assert.False(closed.IsExpectedCloseOverdue);
            Assert.False(closed.IsStaleProgress);
            Assert.False(closed.IsFollowUpOverdue);

            // Missing required data while Open -> DataUnavailable
            Assert.Equal(
                DealHealthStatus.DataUnavailable,
                OpportunityLifecycleRules.EvaluateDealRisk(true, null, asOf, today, asOf, DefaultConfig).Status);
            Assert.Equal(
                DealHealthStatus.DataUnavailable,
                OpportunityLifecycleRules.EvaluateDealRisk(true, today, null, today, asOf, DefaultConfig).Status);
            Assert.Equal(
                DealHealthStatus.DataUnavailable,
                OpportunityLifecycleRules.EvaluateDealRisk(true, today, asOf, null, asOf, DefaultConfig).Status);

            // Exact 14-day boundary -> Healthy (strictly > 14 days triggers stale)
            var exactBoundary = OpportunityLifecycleRules.EvaluateDealRisk(
                true,
                today,
                asOf.AddDays(-14),
                today,
                asOf,
                DefaultConfig);
            Assert.Equal(DealHealthStatus.Healthy, exactBoundary.Status);
            Assert.False(exactBoundary.IsExpectedCloseOverdue);
            Assert.False(exactBoundary.IsStaleProgress);
            Assert.False(exactBoundary.IsFollowUpOverdue);

            // 14 days + 1 second -> AtRisk (IsStaleProgress only)
            var staleByOneSecond = OpportunityLifecycleRules.EvaluateDealRisk(
                true,
                today,
                asOf.AddDays(-14).AddSeconds(-1),
                today,
                asOf,
                DefaultConfig);
            Assert.Equal(DealHealthStatus.AtRisk, staleByOneSecond.Status);
            Assert.False(staleByOneSecond.IsExpectedCloseOverdue);
            Assert.True(staleByOneSecond.IsStaleProgress);
            Assert.False(staleByOneSecond.IsFollowUpOverdue);

            // Multi-flag AtRisk
            var multiFlag = OpportunityLifecycleRules.EvaluateDealRisk(
                true,
                yesterday,
                asOf.AddDays(-15),
                yesterday,
                asOf,
                DefaultConfig);
            Assert.Equal(DealHealthStatus.AtRisk, multiFlag.Status);
            Assert.True(multiFlag.IsExpectedCloseOverdue);
            Assert.True(multiFlag.IsStaleProgress);
            Assert.True(multiFlag.IsFollowUpOverdue);
        }
    }
}
