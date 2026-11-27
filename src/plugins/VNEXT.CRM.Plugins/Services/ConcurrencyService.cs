using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using VNEXT.CRM.Plugins.Core;

namespace VNEXT.CRM.Plugins.Services
{
    public static class ConcurrencyService
    {
        public const int ErrorCodeConcurrencyVersionMismatch = -2147088254;      // 0x80060882
        public const int ErrorCodeConcurrencyVersionNotProvided = -2147088253;   // 0x80060883
        public const int ErrorCodeConcurrencyVersionMismatchAlt = -2147088243;   // 0x8006088D

        public static string NormalizeRowVersion(string rawRowVersion, Guid correlationId)
        {
            if (string.IsNullOrWhiteSpace(rawRowVersion))
            {
                CrmError.Throw(
                    CrmError.CRM021,
                    "ExpectedRowVersion is required for optimistic concurrency verification.",
                    correlationId);
            }

            var value = rawRowVersion.Trim();
            if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(2).Trim();
            }

            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2).Trim();
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                CrmError.Throw(
                    CrmError.CRM021,
                    "ExpectedRowVersion is empty or invalid after normalization.",
                    correlationId);
            }

            return value;
        }

        public static void EnsureSynchronousTransaction(IPluginExecutionContext context)
        {
            if (context == null)
            {
                CrmError.Throw(
                    CrmError.CRM006,
                    "Plugin execution context is unavailable.",
                    Guid.Empty);
            }

            if (context.Mode != 0 || !context.IsInTransaction)
            {
                CrmError.Throw(
                    CrmError.CRM006,
                    "Operation must execute synchronously inside a Dataverse transaction.",
                    context.CorrelationId);
            }
        }

        public static void UpdateIfRowVersionMatches(
            IOrganizationService service,
            Entity target,
            string expectedRowVersion,
            Guid correlationId)
        {
            if (service == null)
            {
                CrmError.Throw(CrmError.CRM008, "Organization service is unavailable.", correlationId);
            }

            if (target == null)
            {
                CrmError.Throw(CrmError.CRM008, "Update target entity is required.", correlationId);
            }

            var normalized = NormalizeRowVersion(expectedRowVersion, correlationId);
            target.RowVersion = normalized;

            var request = new UpdateRequest
            {
                Target = target,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            };

            try
            {
                service.Execute(request);
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                if (IsConcurrencyFault(ex))
                {
                    CrmError.Throw(
                        CrmError.CRM021,
                        "Record was modified by another operation. Refresh the record and retry.",
                        correlationId);
                }

                throw;
            }
        }

        public static int BumpOpportunityCommandSequence(
            IOrganizationService service,
            Guid opportunityId,
            string expectedRowVersion,
            int currentCommandSequence,
            Guid correlationId)
        {
            if (opportunityId == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM006, "Opportunity identifier is required.", correlationId);
            }

            int nextSequence;
            try
            {
                nextSequence = checked(currentCommandSequence + 1);
            }
            catch (OverflowException)
            {
                CrmError.Throw(
                    CrmError.CRM009,
                    "Opportunity command sequence exceeded supported bounds.",
                    correlationId);
                return 0;
            }

            var target = new Entity(CrmSchema.Entities.Opportunity, opportunityId);
            target[CrmSchema.Opportunity.CommandSequence] = nextSequence;

            UpdateIfRowVersionMatches(service, target, expectedRowVersion, correlationId);
            return nextSequence;
        }

        private static bool IsConcurrencyFault(FaultException<OrganizationServiceFault> ex)
        {
            if (ex == null)
            {
                return false;
            }

            if (ex.Detail != null)
            {
                var code = ex.Detail.ErrorCode;
                if (code == ErrorCodeConcurrencyVersionMismatch
                    || code == ErrorCodeConcurrencyVersionNotProvided
                    || code == ErrorCodeConcurrencyVersionMismatchAlt)
                {
                    return true;
                }

                if (MessageIndicatesRowVersionMismatch(ex.Detail.Message))
                {
                    return true;
                }
            }

            return MessageIndicatesRowVersionMismatch(ex.Message);
        }

        private static bool MessageIndicatesRowVersionMismatch(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return (message.IndexOf("rowversion", StringComparison.OrdinalIgnoreCase) >= 0)
                || (message.IndexOf("optimistic concurrency", StringComparison.OrdinalIgnoreCase) >= 0)
                || (message.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0
                    && message.IndexOf("mismatch", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
