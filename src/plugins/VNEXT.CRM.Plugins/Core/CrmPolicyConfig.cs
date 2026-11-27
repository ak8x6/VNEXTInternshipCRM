using System;
using System.Collections.Generic;
using System.Globalization;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace VNEXT.CRM.Plugins.Core
{
    public sealed class CrmPolicyConfig
    {
        public const decimal DefaultApprovalAmount = 50000m;
        public const decimal DefaultApprovalDiscount = 10m;
        public const decimal DefaultMaxDiscount = 30m;
        public const int DefaultStaleDays = 14;
        public const int DefaultEscalationDays = 7;

        public decimal ApprovalAmount { get; }
        public decimal ApprovalDiscount { get; }
        public decimal MaxDiscount { get; }
        public int StaleDays { get; }
        public int EscalationDays { get; }
        public Guid? InternalWriterUserId { get; }
        public Guid BaseCurrencyId { get; }

        public CrmPolicyConfig(
            decimal approvalAmount,
            decimal approvalDiscount,
            decimal maxDiscount,
            int staleDays,
            int escalationDays,
            Guid? internalWriterUserId,
            Guid baseCurrencyId)
        {
            ApprovalAmount = approvalAmount;
            ApprovalDiscount = approvalDiscount;
            MaxDiscount = maxDiscount;
            StaleDays = staleDays;
            EscalationDays = escalationDays;
            InternalWriterUserId = internalWriterUserId;
            BaseCurrencyId = baseCurrencyId;
        }

        public static CrmPolicyConfig CreateDefaults(Guid baseCurrencyId, Guid? internalWriterUserId = null)
        {
            return new CrmPolicyConfig(
                DefaultApprovalAmount,
                DefaultApprovalDiscount,
                DefaultMaxDiscount,
                DefaultStaleDays,
                DefaultEscalationDays,
                internalWriterUserId,
                baseCurrencyId);
        }
    }

    public static class CrmPolicyConfigLoader
    {
        private static readonly string[] PolicyEnvVarNames =
        {
            CrmSchema.EnvVars.ApprovalAmount,
            CrmSchema.EnvVars.ApprovalDiscount,
            CrmSchema.EnvVars.MaxDiscount,
            CrmSchema.EnvVars.StaleDays,
            CrmSchema.EnvVars.EscalationDays,
            CrmSchema.EnvVars.InternalWriterUserId
        };

        public static CrmPolicyConfig Load(IOrganizationService service, Guid correlationId)
        {
            if (service == null)
            {
                CrmError.Throw(CrmError.CRM008, "Organization service is unavailable while loading CRM policy configuration.", correlationId);
            }

            var rawValues = LoadRawEnvironmentVariables(service);
            var baseCurrencyId = ResolveBaseCurrencyId(service, correlationId);
            return Parse(rawValues, baseCurrencyId, correlationId);
        }

        public static CrmPolicyConfig Parse(
            IDictionary<string, string> rawValues,
            Guid baseCurrencyId,
            Guid correlationId)
        {
            if (baseCurrencyId == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM008, "Base currency configuration is missing or invalid.", correlationId);
            }

            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (rawValues != null)
            {
                foreach (var kvp in rawValues)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key))
                    {
                        lookup[kvp.Key.Trim()] = kvp.Value;
                    }
                }
            }

            var approvalAmount = ParseDecimal(
                lookup,
                CrmSchema.EnvVars.ApprovalAmount,
                CrmPolicyConfig.DefaultApprovalAmount,
                correlationId);

            var maxDiscount = ParseDecimal(
                lookup,
                CrmSchema.EnvVars.MaxDiscount,
                CrmPolicyConfig.DefaultMaxDiscount,
                correlationId);

            var approvalDiscount = ParseDecimal(
                lookup,
                CrmSchema.EnvVars.ApprovalDiscount,
                CrmPolicyConfig.DefaultApprovalDiscount,
                correlationId);

            var staleDays = ParsePositiveInt(
                lookup,
                CrmSchema.EnvVars.StaleDays,
                CrmPolicyConfig.DefaultStaleDays,
                correlationId);

            var escalationDays = ParsePositiveInt(
                lookup,
                CrmSchema.EnvVars.EscalationDays,
                CrmPolicyConfig.DefaultEscalationDays,
                correlationId);

            if (approvalAmount <= 0m)
            {
                CrmError.Throw(CrmError.CRM008, "Policy vn_ApprovalAmount must be greater than zero.", correlationId);
            }

            if (maxDiscount <= 0m || maxDiscount > 100m)
            {
                CrmError.Throw(CrmError.CRM008, "Policy vn_MaxDiscount must be greater than 0 and at most 100.", correlationId);
            }

            if (approvalDiscount < 0m || approvalDiscount > maxDiscount)
            {
                CrmError.Throw(CrmError.CRM008, "Policy vn_ApprovalDiscount must be between 0 and vn_MaxDiscount.", correlationId);
            }

            if (staleDays <= 0)
            {
                CrmError.Throw(CrmError.CRM008, "Policy vn_StaleDays must be a positive whole number.", correlationId);
            }

            if (escalationDays <= 0)
            {
                CrmError.Throw(CrmError.CRM008, "Policy vn_EscalationDays must be a positive whole number.", correlationId);
            }

            Guid? internalWriterUserId = null;
            string rawWriterId;
            if (lookup.TryGetValue(CrmSchema.EnvVars.InternalWriterUserId, out rawWriterId)
                && !string.IsNullOrWhiteSpace(rawWriterId))
            {
                Guid parsedGuid;
                if (!Guid.TryParse(rawWriterId.Trim(), out parsedGuid))
                {
                    CrmError.Throw(CrmError.CRM008, "Configuration vn_InternalWriterUserId is not a valid GUID.", correlationId);
                }

                if (parsedGuid != Guid.Empty)
                {
                    internalWriterUserId = parsedGuid;
                }
            }

            return new CrmPolicyConfig(
                approvalAmount,
                approvalDiscount,
                maxDiscount,
                staleDays,
                escalationDays,
                internalWriterUserId,
                baseCurrencyId);
        }

        private static Dictionary<string, string> LoadRawEnvironmentVariables(IOrganizationService service)
        {
            var query = new QueryExpression(CrmSchema.Entities.EnvironmentVariableDefinition)
            {
                ColumnSet = new ColumnSet(CrmSchema.Platform.EnvVarSchemaName, CrmSchema.Platform.EnvVarDefaultValue),
                NoLock = true
            };

            query.Criteria.AddCondition(
                CrmSchema.Platform.EnvVarSchemaName,
                ConditionOperator.In,
                PolicyEnvVarNames);

            var valueLink = query.AddLink(
                CrmSchema.Entities.EnvironmentVariableValue,
                CrmSchema.Platform.EnvVarDefinitionId,
                CrmSchema.Platform.EnvVarDefinitionId,
                JoinOperator.LeftOuter);
            valueLink.EntityAlias = "val";
            valueLink.Columns = new ColumnSet(CrmSchema.Platform.EnvVarValue);

            var results = service.RetrieveMultiple(query);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in results.Entities)
            {
                var schemaName = entity.GetAttributeValue<string>(CrmSchema.Platform.EnvVarSchemaName);
                if (string.IsNullOrWhiteSpace(schemaName))
                {
                    continue;
                }

                var defaultValue = entity.GetAttributeValue<string>(CrmSchema.Platform.EnvVarDefaultValue);
                string overrideValue = null;
                var aliased = entity.GetAttributeValue<AliasedValue>("val." + CrmSchema.Platform.EnvVarValue);
                if (aliased != null && aliased.Value != null)
                {
                    overrideValue = aliased.Value as string ?? aliased.Value.ToString();
                }

                map[schemaName] = !string.IsNullOrWhiteSpace(overrideValue) ? overrideValue : defaultValue;
            }

            return map;
        }

        private static Guid ResolveBaseCurrencyId(IOrganizationService service, Guid correlationId)
        {
            try
            {
                var orgQuery = new QueryExpression(CrmSchema.Entities.Organization)
                {
                    ColumnSet = new ColumnSet(CrmSchema.Platform.OrganizationBaseCurrencyId),
                    TopCount = 1,
                    NoLock = true
                };

                var orgs = service.RetrieveMultiple(orgQuery);
                if (orgs.Entities.Count > 0)
                {
                    var baseRef = orgs.Entities[0].GetAttributeValue<EntityReference>(CrmSchema.Platform.OrganizationBaseCurrencyId);
                    if (baseRef != null && baseRef.Id != Guid.Empty)
                    {
                        return baseRef.Id;
                    }
                }
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                // Fall back to active base-rate transactioncurrency lookup below when organization read is denied.
            }

            var currencyQuery = new QueryExpression(CrmSchema.Entities.TransactionCurrency)
            {
                ColumnSet = new ColumnSet(CrmSchema.Platform.TransactionCurrencyId),
                TopCount = 2,
                NoLock = true
            };
            currencyQuery.Criteria.AddCondition(CrmSchema.Common.StateCode, ConditionOperator.Equal, 0);
            currencyQuery.Criteria.AddCondition(CrmSchema.Platform.ExchangeRate, ConditionOperator.Equal, 1m);

            var currencies = service.RetrieveMultiple(currencyQuery);
            if (currencies.Entities.Count != 1)
            {
                CrmError.Throw(
                    CrmError.CRM008,
                    "Unable to deterministically resolve the environment base currency.",
                    correlationId);
            }

            return currencies.Entities[0].Id;
        }

        private static decimal ParseDecimal(
            IDictionary<string, string> lookup,
            string key,
            decimal defaultValue,
            Guid correlationId)
        {
            string raw;
            if (!lookup.TryGetValue(key, out raw) || string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            decimal parsed;
            if (!decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
            {
                CrmError.Throw(CrmError.CRM008, string.Format("Policy {0} has an invalid numeric value.", key), correlationId);
            }

            return parsed;
        }

        private static int ParsePositiveInt(
            IDictionary<string, string> lookup,
            string key,
            int defaultValue,
            Guid correlationId)
        {
            string raw;
            if (!lookup.TryGetValue(key, out raw) || string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            decimal parsed;
            if (!decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)
                || decimal.Truncate(parsed) != parsed
                || parsed <= 0m
                || parsed > int.MaxValue)
            {
                CrmError.Throw(CrmError.CRM008, string.Format("Policy {0} must be a positive whole number.", key), correlationId);
            }

            return (int)parsed;
        }
    }
}
