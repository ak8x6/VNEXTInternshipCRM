using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Xrm.Sdk;
using VNEXT.CRM.Plugins.Core;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    public class CoreTests
    {
        private static readonly Guid SampleCurrencyId = new Guid("c6c8babe-05ca-f111-a05a-7ced8d5f8fd9");
        private static readonly Guid SampleCorrelationId = new Guid("11111111-2222-3333-4444-555555555555");

        [Fact]
        public void CrmError_DefinesExactTwentySpecCodes()
        {
            var expected = new[]
            {
                "CRM001", "CRM002", "CRM003", "CRM004", "CRM005",
                "CRM006", "CRM007", "CRM008", "CRM009", "CRM010",
                "CRM012", "CRM013", "CRM014", "CRM015", "CRM016",
                "CRM017", "CRM018", "CRM020", "CRM021", "CRM022"
            };

            var actual = typeof(CrmError)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue())
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(20, actual.Length);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CrmError_FormatAndCreate_ProduceSingleLineSpecMessage()
        {
            var formatted = CrmError.Format(CrmError.CRM021, "Record changed.\r\nRefresh before retry.", SampleCorrelationId);
            Assert.Equal(
                "[CRM021] Record changed.  Refresh before retry. (CorrelationId: 11111111-2222-3333-4444-555555555555)",
                formatted);
            Assert.DoesNotContain("\r", formatted);
            Assert.DoesNotContain("\n", formatted);

            var ex = CrmError.Create(CrmError.CRM005, "Direct Opportunity Create is denied.", SampleCorrelationId);
            Assert.Equal(OperationStatus.Failed, ex.Status);
            Assert.Equal(
                "[CRM005] Direct Opportunity Create is denied. (CorrelationId: 11111111-2222-3333-4444-555555555555)",
                ex.Message);
        }

        [Fact]
        public void UtcClock_EnsureUtc_AndNormalizeDateOnly_HandleUtcUnspecifiedAndLocal()
        {
            var utcInput = new DateTime(2026, 11, 23, 23, 45, 0, DateTimeKind.Utc);
            Assert.Equal(utcInput, UtcClock.EnsureUtc(utcInput));
            Assert.Equal(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc), UtcClock.TodayUtc(utcInput));
            Assert.Equal(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc), UtcClock.NormalizeDateOnly(utcInput));

            var unspecifiedInput = new DateTime(2026, 11, 23, 18, 30, 0, DateTimeKind.Unspecified);
            var ensuredUnspecified = UtcClock.EnsureUtc(unspecifiedInput);
            Assert.Equal(DateTimeKind.Utc, ensuredUnspecified.Kind);
            Assert.Equal(new DateTime(2026, 11, 23, 18, 30, 0, DateTimeKind.Utc), ensuredUnspecified);
            var normalizedUnspecified = UtcClock.NormalizeDateOnly(unspecifiedInput);
            Assert.Equal(DateTimeKind.Utc, normalizedUnspecified.Kind);
            Assert.Equal(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc), normalizedUnspecified);

            var localInput = DateTime.SpecifyKind(new DateTime(2026, 11, 23, 0, 15, 0), DateTimeKind.Local);
            var expectedUtc = localInput.ToUniversalTime();
            Assert.Equal(expectedUtc, UtcClock.EnsureUtc(localInput));
            var normalizedLocal = UtcClock.NormalizeDateOnly(localInput);
            Assert.Equal(DateTimeKind.Utc, normalizedLocal.Kind);
            Assert.Equal(expectedUtc.Date, normalizedLocal.Date);
        }

        [Fact]
        public void CrmPolicyConfigLoader_Parse_UsesSpecDefaultsWhenRawValuesEmpty()
        {
            var cfg = CrmPolicyConfigLoader.Parse(new Dictionary<string, string>(), SampleCurrencyId, SampleCorrelationId);

            Assert.Equal(50000m, cfg.ApprovalAmount);
            Assert.Equal(10m, cfg.ApprovalDiscount);
            Assert.Equal(30m, cfg.MaxDiscount);
            Assert.Equal(14, cfg.StaleDays);
            Assert.Equal(7, cfg.EscalationDays);
            Assert.Null(cfg.InternalWriterUserId);
            Assert.Equal(SampleCurrencyId, cfg.BaseCurrencyId);
        }

        [Fact]
        public void CrmPolicyConfigLoader_Parse_AppliesOverridesUsingInvariantCultureUnderFrFr()
        {
            var writerGuid = new Guid("f3f85cec-2de7-f111-84ea-002248db522e");
            var raw = new Dictionary<string, string>
            {
                { CrmSchema.EnvVars.ApprovalAmount, "75000.50" },
                { CrmSchema.EnvVars.ApprovalDiscount, "12.50" },
                { CrmSchema.EnvVars.MaxDiscount, "25.75" },
                { CrmSchema.EnvVars.StaleDays, "21" },
                { CrmSchema.EnvVars.EscalationDays, "5.0" },
                { CrmSchema.EnvVars.InternalWriterUserId, writerGuid.ToString("D") }
            };

            var previousCulture = Thread.CurrentThread.CurrentCulture;
            var previousUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                var fr = CultureInfo.GetCultureInfo("fr-FR");
                Thread.CurrentThread.CurrentCulture = fr;
                Thread.CurrentThread.CurrentUICulture = fr;

                var cfg = CrmPolicyConfigLoader.Parse(raw, SampleCurrencyId, SampleCorrelationId);

                Assert.Equal(75000.50m, cfg.ApprovalAmount);
                Assert.Equal(12.50m, cfg.ApprovalDiscount);
                Assert.Equal(25.75m, cfg.MaxDiscount);
                Assert.Equal(21, cfg.StaleDays);
                Assert.Equal(5, cfg.EscalationDays);
                Assert.Equal(writerGuid, cfg.InternalWriterUserId);
                Assert.Equal(SampleCurrencyId, cfg.BaseCurrencyId);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
                Thread.CurrentThread.CurrentUICulture = previousUiCulture;
            }
        }

        [Theory]
        [InlineData("vn_ApprovalAmount", "0")]
        [InlineData("vn_ApprovalAmount", "-100")]
        [InlineData("vn_ApprovalAmount", "not-a-number")]
        [InlineData("vn_MaxDiscount", "0")]
        [InlineData("vn_MaxDiscount", "100.01")]
        [InlineData("vn_ApprovalDiscount", "-0.01")]
        [InlineData("vn_ApprovalDiscount", "31")]
        [InlineData("vn_StaleDays", "0")]
        [InlineData("vn_StaleDays", "14.5")]
        [InlineData("vn_EscalationDays", "-1")]
        [InlineData("vn_InternalWriterUserId", "not-a-guid")]
        public void CrmPolicyConfigLoader_Parse_RejectsMalformedOrOutOfBoundsValues(string key, string badValue)
        {
            var raw = new Dictionary<string, string> { { key, badValue } };

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => CrmPolicyConfigLoader.Parse(raw, SampleCurrencyId, SampleCorrelationId));

            Assert.Contains("[CRM008]", ex.Message);
            Assert.Contains(SampleCorrelationId.ToString("D"), ex.Message);
        }

        [Fact]
        public void CrmPolicyConfigLoader_Parse_HandlesMissingBlankAndEmptyInternalWriterGuid()
        {
            var cfgMissing = CrmPolicyConfigLoader.Parse(new Dictionary<string, string>(), SampleCurrencyId, SampleCorrelationId);
            Assert.Null(cfgMissing.InternalWriterUserId);

            var cfgBlank = CrmPolicyConfigLoader.Parse(
                new Dictionary<string, string> { { CrmSchema.EnvVars.InternalWriterUserId, "   " } },
                SampleCurrencyId,
                SampleCorrelationId);
            Assert.Null(cfgBlank.InternalWriterUserId);

            var cfgEmptyGuid = CrmPolicyConfigLoader.Parse(
                new Dictionary<string, string> { { CrmSchema.EnvVars.InternalWriterUserId, Guid.Empty.ToString("D") } },
                SampleCurrencyId,
                SampleCorrelationId);
            Assert.Null(cfgEmptyGuid.InternalWriterUserId);

            Assert.Throws<InvalidPluginExecutionException>(
                () => CrmPolicyConfigLoader.Parse(new Dictionary<string, string>(), Guid.Empty, SampleCorrelationId));
        }

        [Fact]
        public void CrmSchema_SanityChecks_BpfStagesApprovalResultsAndProbabilities()
        {
            var stages = new[]
            {
                CrmSchema.Bpf.StageQualifyId,
                CrmSchema.Bpf.StageDevelopId,
                CrmSchema.Bpf.StageProposeId,
                CrmSchema.Bpf.StageCloseId
            };
            Assert.Equal(4, stages.Distinct().Count());
            Assert.All(stages, id => Assert.NotEqual(Guid.Empty, id));

            Assert.Equal(100000000, CrmSchema.ApprovalRequest.ResultValues.Pending);
            Assert.Equal(100000001, CrmSchema.ApprovalRequest.ResultValues.Approved);
            Assert.Equal(100000002, CrmSchema.ApprovalRequest.ResultValues.Rejected);
            Assert.Equal(100000003, CrmSchema.ApprovalRequest.ResultValues.Superseded);
            Assert.Equal(100000005, CrmSchema.ApprovalRequest.ResultValues.Cancelled);

            Assert.Equal(10, CrmSchema.Opportunity.Probabilities.Qualify);
            Assert.Equal(30, CrmSchema.Opportunity.Probabilities.Develop);
            Assert.Equal(60, CrmSchema.Opportunity.Probabilities.Propose);
            Assert.Equal(90, CrmSchema.Opportunity.Probabilities.Close);
            Assert.Equal(100, CrmSchema.Opportunity.Probabilities.Won);
            Assert.Equal(0, CrmSchema.Opportunity.Probabilities.Lost);
        }
    }
}
