using System;
using Microsoft.Xrm.Sdk;

namespace VNEXT.CRM.Plugins.Core
{
    public static class CrmError
    {
        public const string CRM001 = "CRM001";
        public const string CRM002 = "CRM002";
        public const string CRM003 = "CRM003";
        public const string CRM004 = "CRM004";
        public const string CRM005 = "CRM005";
        public const string CRM006 = "CRM006";
        public const string CRM007 = "CRM007";
        public const string CRM008 = "CRM008";
        public const string CRM009 = "CRM009";
        public const string CRM010 = "CRM010";
        public const string CRM012 = "CRM012";
        public const string CRM013 = "CRM013";
        public const string CRM014 = "CRM014";
        public const string CRM015 = "CRM015";
        public const string CRM016 = "CRM016";
        public const string CRM017 = "CRM017";
        public const string CRM018 = "CRM018";
        public const string CRM020 = "CRM020";
        public const string CRM021 = "CRM021";
        public const string CRM022 = "CRM022";

        public static string Format(string code, string message, Guid correlationId)
        {
            var safeCode = string.IsNullOrWhiteSpace(code) ? CRM008 : code.Trim();
            var safeMessage = string.IsNullOrWhiteSpace(message)
                ? "The requested operation could not be completed."
                : message.Replace("\r", " ").Replace("\n", " ").Trim();

            return string.Format("[{0}] {1} (CorrelationId: {2:D})", safeCode, safeMessage, correlationId);
        }

        public static InvalidPluginExecutionException Create(string code, string message, Guid correlationId)
        {
            return new InvalidPluginExecutionException(OperationStatus.Failed, Format(code, message, correlationId));
        }

        public static void Throw(string code, string message, Guid correlationId)
        {
            throw Create(code, message, correlationId);
        }
    }
}
