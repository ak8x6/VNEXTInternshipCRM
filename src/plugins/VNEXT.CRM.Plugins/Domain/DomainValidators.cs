using System;
using VNEXT.CRM.Plugins.Core;

namespace VNEXT.CRM.Plugins.Domain
{
    public enum CustomerSelectionPath
    {
        ReuseAccountAndContact,
        ReuseAccountCreateContact,
        CreateAccountAndContact
    }

    public static class DomainValidators
    {
        public const int MaxSkuLength = 50;
        public const int MaxEmailLength = 254;

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            var trimmed = email.Trim();
            if (trimmed.Length == 0 || trimmed.Length > MaxEmailLength)
            {
                return false;
            }

            int atIndex = -1;
            for (int i = 0; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];
                if (char.IsWhiteSpace(ch))
                {
                    return false;
                }

                if (ch == '@')
                {
                    if (atIndex >= 0)
                    {
                        return false;
                    }

                    atIndex = i;
                }
            }

            if (atIndex <= 0 || atIndex >= trimmed.Length - 1)
            {
                return false;
            }

            var host = trimmed.Substring(atIndex + 1);
            if (host.IndexOf('.') < 0 || host[0] == '.' || host[host.Length - 1] == '.')
            {
                return false;
            }

            var labels = host.Split('.');
            if (labels.Length < 2)
            {
                return false;
            }

            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i].Length == 0)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsValidSku(string sku)
        {
            if (string.IsNullOrEmpty(sku) || sku.Length > MaxSkuLength)
            {
                return false;
            }

            for (int i = 0; i < sku.Length; i++)
            {
                var ch = sku[i];
                bool isUpperAscii = ch >= 'A' && ch <= 'Z';
                bool isAsciiDigit = ch >= '0' && ch <= '9';
                bool isHyphen = ch == '-';
                if (!isUpperAscii && !isAsciiDigit && !isHyphen)
                {
                    return false;
                }
            }

            return true;
        }

        public static void ValidateProduct(
            string name,
            string sku,
            string description,
            decimal? unitPrice,
            Guid? currencyId,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                CrmError.Throw(CrmError.CRM008, "Product name is required.", correlationId);
            }

            if (!IsValidSku(sku))
            {
                CrmError.Throw(
                    CrmError.CRM008,
                    "Product SKU must be 1 to 50 uppercase A-Z, 0-9, or hyphen characters without whitespace.",
                    correlationId);
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                CrmError.Throw(CrmError.CRM008, "Product description is required.", correlationId);
            }

            DomainPricing.ValidateBaseCurrency(currencyId, config, correlationId);

            if (!unitPrice.HasValue)
            {
                CrmError.Throw(CrmError.CRM008, "Product unit price is required.", correlationId);
            }

            DomainPricing.ValidateProductUnitPrice(unitPrice.Value, config, correlationId);
        }

        public static void ValidateAccount(string name, int? accountType, int? industry, Guid correlationId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                CrmError.Throw(CrmError.CRM001, "Account name is required.", correlationId);
            }

            if (!accountType.HasValue || !IsValidAccountType(accountType.Value))
            {
                CrmError.Throw(CrmError.CRM001, "Account type must be Prospect, Customer, or Partner.", correlationId);
            }

            if (industry.HasValue && !IsValidIndustry(industry.Value))
            {
                CrmError.Throw(CrmError.CRM001, "Account industry is not a supported choice value.", correlationId);
            }
        }

        public static void ValidateContact(
            string lastName,
            string parentLogicalName,
            Guid? parentId,
            string email,
            string phone,
            int? preferredMethod,
            Guid correlationId)
        {
            if (!string.Equals(parentLogicalName, CrmSchema.Entities.Account, StringComparison.OrdinalIgnoreCase)
                || !parentId.HasValue
                || parentId.Value == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM004, "Contact must belong to an active parent Account.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(lastName))
            {
                CrmError.Throw(CrmError.CRM001, "Contact last name is required.", correlationId);
            }

            var hasEmail = !string.IsNullOrWhiteSpace(email);
            var hasPhone = !string.IsNullOrWhiteSpace(phone);
            if (!hasEmail && !hasPhone)
            {
                CrmError.Throw(CrmError.CRM001, "Contact requires at least an email address or a phone number.", correlationId);
            }

            if (hasEmail && !IsValidEmail(email))
            {
                CrmError.Throw(CrmError.CRM001, "Contact email address format is invalid.", correlationId);
            }

            if (!preferredMethod.HasValue || !IsValidPreferredMethod(preferredMethod.Value))
            {
                CrmError.Throw(CrmError.CRM001, "Contact preferred communication method must be Email or Phone.", correlationId);
            }

            if (preferredMethod.Value == CrmSchema.Contact.PreferredMethodValues.Email && !hasEmail)
            {
                CrmError.Throw(CrmError.CRM001, "Preferred method Email requires a nonblank email address.", correlationId);
            }

            if (preferredMethod.Value == CrmSchema.Contact.PreferredMethodValues.Phone && !hasPhone)
            {
                CrmError.Throw(CrmError.CRM001, "Preferred method Phone requires a nonblank phone number.", correlationId);
            }
        }

        public static int ResolveDefaultPreferredMethod(string email, string phone)
        {
            return !string.IsNullOrWhiteSpace(email)
                ? CrmSchema.Contact.PreferredMethodValues.Email
                : CrmSchema.Contact.PreferredMethodValues.Phone;
        }

        public static void ValidateOpenLead(
            string topic,
            string lastName,
            int? source,
            int? rating,
            string email,
            string phone,
            DateTime? nextFollowUpDate,
            decimal? budget,
            Guid? currencyId,
            int statusCode,
            string nextStep,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (statusCode != CrmSchema.Lead.Status.New && statusCode != CrmSchema.Lead.Status.Contacted)
            {
                CrmError.Throw(CrmError.CRM001, "Open Lead status must be New or Contacted.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(topic))
            {
                CrmError.Throw(CrmError.CRM001, "Lead topic is required.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(lastName))
            {
                CrmError.Throw(CrmError.CRM001, "Lead last name is required.", correlationId);
            }

            if (!source.HasValue || !IsValidLeadSource(source.Value))
            {
                CrmError.Throw(CrmError.CRM001, "Lead source must be Website, Referral, Cold Call, or Event.", correlationId);
            }

            if (!rating.HasValue || !IsValidLeadRating(rating.Value))
            {
                CrmError.Throw(CrmError.CRM001, "Lead rating must be Hot, Warm, or Cold.", correlationId);
            }

            var hasEmail = !string.IsNullOrWhiteSpace(email);
            var hasPhone = !string.IsNullOrWhiteSpace(phone);
            if (!hasEmail && !hasPhone)
            {
                CrmError.Throw(CrmError.CRM001, "Lead requires at least an email address or a phone number.", correlationId);
            }

            if (hasEmail && !IsValidEmail(email))
            {
                CrmError.Throw(CrmError.CRM001, "Lead email address format is invalid.", correlationId);
            }

            if (!nextFollowUpDate.HasValue)
            {
                CrmError.Throw(CrmError.CRM001, "Lead next follow-up date is required while Open.", correlationId);
            }

            DomainPricing.ValidateOptionalBudget(budget, currencyId, config, correlationId);

            if (statusCode == CrmSchema.Lead.Status.Contacted && string.IsNullOrWhiteSpace(nextStep))
            {
                CrmError.Throw(CrmError.CRM001, "Contacted Lead requires a nonblank next step.", correlationId);
            }
        }

        public static void ValidateLeadDisqualification(
            Guid leadId,
            int? reason,
            string comments,
            Guid? duplicateOfId,
            Guid correlationId)
        {
            if (!reason.HasValue || !IsValidDisqualificationReason(reason.Value))
            {
                CrmError.Throw(
                    CrmError.CRM001,
                    "Disqualification reason must be No Fit, No Budget, No Response, or Duplicate.",
                    correlationId);
            }

            if (string.IsNullOrWhiteSpace(comments))
            {
                CrmError.Throw(CrmError.CRM001, "Disqualification comments are required.", correlationId);
            }

            if (reason.Value == CrmSchema.Lead.DisqualificationReasonValues.Duplicate)
            {
                if (!duplicateOfId.HasValue || duplicateOfId.Value == Guid.Empty)
                {
                    CrmError.Throw(CrmError.CRM001, "Duplicate disqualification requires a referenced Lead.", correlationId);
                }

                if (duplicateOfId.Value == leadId)
                {
                    CrmError.Throw(CrmError.CRM001, "A Lead cannot reference itself as Duplicate Of.", correlationId);
                }
            }
            else if (duplicateOfId.HasValue)
            {
                CrmError.Throw(
                    CrmError.CRM001,
                    "Duplicate Of may only be supplied when disqualification reason is Duplicate.",
                    correlationId);
            }
        }

        public static void ValidateLeadQualificationReadiness(
            int stateCode,
            int statusCode,
            string companyName,
            string lastName,
            string email,
            string phone,
            string need,
            string nextStep,
            DateTime? nextFollowUpDate,
            decimal? budget,
            Guid? currencyId,
            string opportunityName,
            DateTime? expectedCloseDate,
            DateTime utcNow,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (stateCode != CrmSchema.Lead.State.Active || statusCode != CrmSchema.Lead.Status.Contacted)
            {
                CrmError.Throw(CrmError.CRM002, "Only an Active Contacted Lead can be qualified.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(companyName))
            {
                CrmError.Throw(CrmError.CRM002, "Company name is required to qualify a Lead.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(lastName))
            {
                CrmError.Throw(CrmError.CRM002, "Last name is required to qualify a Lead.", correlationId);
            }

            var hasEmail = !string.IsNullOrWhiteSpace(email);
            var hasPhone = !string.IsNullOrWhiteSpace(phone);
            if (!hasEmail && !hasPhone)
            {
                CrmError.Throw(CrmError.CRM002, "Email or phone is required to qualify a Lead.", correlationId);
            }

            if (hasEmail && !IsValidEmail(email))
            {
                CrmError.Throw(CrmError.CRM002, "Lead email address format is invalid.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(need))
            {
                CrmError.Throw(CrmError.CRM002, "Business need is required to qualify a Lead.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(nextStep))
            {
                CrmError.Throw(CrmError.CRM002, "Next step is required to qualify a Lead.", correlationId);
            }

            if (!nextFollowUpDate.HasValue)
            {
                CrmError.Throw(CrmError.CRM002, "Next follow-up date is required to qualify a Lead.", correlationId);
            }

            DomainPricing.ValidateBaseCurrency(currencyId, config, correlationId);

            if (budget.HasValue)
            {
                var value = budget.Value;
                if (value < 0m || value > DomainPricing.MaxBudget || !DomainPricing.HasAtMostDecimalPlaces(value, DomainPricing.BudgetDecimalPlaces))
                {
                    CrmError.Throw(
                        CrmError.CRM002,
                        "Lead budget must be between 0 and 1,000,000,000 with at most 2 decimal places.",
                        correlationId);
                }
            }

            if (string.IsNullOrWhiteSpace(opportunityName))
            {
                CrmError.Throw(CrmError.CRM002, "Opportunity name is required to qualify a Lead.", correlationId);
            }

            if (!expectedCloseDate.HasValue)
            {
                CrmError.Throw(CrmError.CRM002, "Expected close date is required to qualify a Lead.", correlationId);
            }

            var normalizedExpectedClose = UtcClock.NormalizeDateOnly(expectedCloseDate.Value);
            var utcToday = UtcClock.TodayUtc(utcNow);
            if (normalizedExpectedClose < utcToday)
            {
                CrmError.Throw(
                    CrmError.CRM002,
                    "Expected close date must be on or after UTC today.",
                    correlationId);
            }
        }

        public static CustomerSelectionPath ResolveCustomerSelectionPath(
            Guid? existingAccountId,
            Guid? existingContactId,
            Guid correlationId)
        {
            if ((existingAccountId.HasValue && existingAccountId.Value == Guid.Empty)
                || (existingContactId.HasValue && existingContactId.Value == Guid.Empty))
            {
                CrmError.Throw(
                    CrmError.CRM003,
                    "Existing customer identifiers cannot be an empty GUID.",
                    correlationId);
            }

            var hasAccount = existingAccountId.HasValue;
            var hasContact = existingContactId.HasValue;

            if (hasAccount && hasContact)
            {
                return CustomerSelectionPath.ReuseAccountAndContact;
            }

            if (hasAccount)
            {
                return CustomerSelectionPath.ReuseAccountCreateContact;
            }

            if (!hasContact)
            {
                return CustomerSelectionPath.CreateAccountAndContact;
            }

            CrmError.Throw(
                CrmError.CRM004,
                "An existing Contact cannot be selected without its parent Account.",
                correlationId);
            return default(CustomerSelectionPath);
        }

        private static bool IsValidAccountType(int value)
        {
            return value == CrmSchema.Account.AccountTypeValues.Prospect
                || value == CrmSchema.Account.AccountTypeValues.Customer
                || value == CrmSchema.Account.AccountTypeValues.Partner;
        }

        private static bool IsValidIndustry(int value)
        {
            return value == CrmSchema.Account.IndustryValues.Manufacturing
                || value == CrmSchema.Account.IndustryValues.Services
                || value == CrmSchema.Account.IndustryValues.Retail
                || value == CrmSchema.Account.IndustryValues.FinancialServices
                || value == CrmSchema.Account.IndustryValues.Other;
        }

        private static bool IsValidPreferredMethod(int value)
        {
            return value == CrmSchema.Contact.PreferredMethodValues.Email
                || value == CrmSchema.Contact.PreferredMethodValues.Phone;
        }

        private static bool IsValidLeadSource(int value)
        {
            return value == CrmSchema.Lead.SourceValues.Website
                || value == CrmSchema.Lead.SourceValues.Referral
                || value == CrmSchema.Lead.SourceValues.ColdCall
                || value == CrmSchema.Lead.SourceValues.Event;
        }

        private static bool IsValidLeadRating(int value)
        {
            return value == CrmSchema.Lead.RatingValues.Hot
                || value == CrmSchema.Lead.RatingValues.Warm
                || value == CrmSchema.Lead.RatingValues.Cold;
        }

        private static bool IsValidDisqualificationReason(int value)
        {
            return value == CrmSchema.Lead.DisqualificationReasonValues.NoFit
                || value == CrmSchema.Lead.DisqualificationReasonValues.NoBudget
                || value == CrmSchema.Lead.DisqualificationReasonValues.NoResponse
                || value == CrmSchema.Lead.DisqualificationReasonValues.Duplicate;
        }
    }
}
