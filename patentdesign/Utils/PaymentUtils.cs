using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Bogus.DataSets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Utils;

public class PaymentUtils(IOptions<PaymentInfo> remitaPaymentDetails, ILogger<PaymentUtils> log)
{
    private PaymentInfo _paymentInfo = remitaPaymentDetails.Value;
    private readonly ILogger<PaymentUtils> _log = log;

    public  (string, string, string)  GetCost(PaymentTypes type,  FileTypes? fileType, string applicantNationality, DesignTypes? designType=null, PatentTypes? patentType=null, string? patentChangeType=null)
    {
        string amount = "";
        string serviceId = "";
        var serviceFee = "";
        switch (type)
        {
            case PaymentTypes.TrademarkCertificate:
                amount = _paymentInfo.TrademarkCertificateFee;
                serviceFee = _paymentInfo.TrademarkCertificateServiceFee;
                serviceId = _paymentInfo.TrademarkCertificateServiceId;
                break;
            case PaymentTypes.OppositionCreation:
                amount = _paymentInfo.OppositionCreationCost;
                serviceFee = _paymentInfo.OppositionCreationServiceFee;
                serviceId = _paymentInfo.OppositionCreationID;
                break;
            case PaymentTypes.Assignment:
                amount = _paymentInfo.AssignmentAppCost;
                serviceFee = _paymentInfo.AssignmentServiceFee;
                serviceId = _paymentInfo.AssignmentID;
                break;
            case PaymentTypes.NewCreation:
                if (fileType is FileTypes.Design)
                {
                    if (designType is DesignTypes.NonTextile)
                    {
                        amount = _paymentInfo.DesignCreationNonTextileCost;
                        serviceId = _paymentInfo.DesignCreationNonTextileID;
                        serviceFee = _paymentInfo.DesignCreationNonTextileServiceFee;
                    }

                    else
                    {
                        amount = _paymentInfo.DesignCreationTextileCost;
                        serviceId = _paymentInfo.DesignCreationTextileID;
                        serviceFee = _paymentInfo.DesignCreationTextileServiceFee;
                    }
                }

                else if (fileType is FileTypes.Patent)
                {
                    if (patentType is PatentTypes.Conventional or PatentTypes.PCT)
                    {
                        amount = _paymentInfo.PatentCreationConventionalCost;
                        serviceId = _paymentInfo.PatentCreationConventionalID;
                        serviceFee = _paymentInfo.PatentCreationConventionalServiceFee;
                    }
                    else
                    {
                        if (string.Equals(applicantNationality, "Nigeria", StringComparison.CurrentCultureIgnoreCase))
                        {
                            amount = _paymentInfo.PatentCreationNonConventionalCost;
                            serviceId = _paymentInfo.PatentCreationNonConventionalID;
                            serviceFee = _paymentInfo.PatentCreationNonConventionalServiceFee;
                        }
                        else
                        {
                            amount = _paymentInfo.PatentCreationConventionalCost;
                            serviceId = _paymentInfo.PatentCreationConventionalID;
                            serviceFee = _paymentInfo.PatentCreationConventionalServiceFee;
                        }

                    }
                }

                else if (fileType is FileTypes.TradeMark)
                {
                    amount = _paymentInfo.TrademarkRegistrationCost;
                    serviceId = _paymentInfo.TrademarkRegistrationID;
                    serviceFee = _paymentInfo.TrademarkRegistrationServiceFee;
                }

                break;
            case PaymentTypes.Search:
                if (fileType == FileTypes.Design)
                {
                    amount = _paymentInfo.DesignSearchCost;
                    serviceId = _paymentInfo.DesignSearchID;
                    serviceFee = _paymentInfo.DesignSearchServiceFee;


                }
                else
                {
                    amount = _paymentInfo.PatentSearchCost;
                    serviceId = _paymentInfo.PatentSearchID;
                    serviceFee = _paymentInfo.PatentSearchServiceFee;

                }

                break;
            case PaymentTypes.AvailabilitySearch:
                amount = _paymentInfo.AvailabilitySearchCost;
                serviceId = _paymentInfo.AvailabilitySearchServiceID;
                serviceFee = _paymentInfo.AvailabilitySearchServiceFee;
                break;
            case PaymentTypes.Merger:
                amount = _paymentInfo.MergerCost;
                serviceId = _paymentInfo.MergerServiceID;
                serviceFee = _paymentInfo.MergerServiceFee;
                break;
            case PaymentTypes.ChangeDataRecordal:
                amount = _paymentInfo.ChangeDataRecordalCost;
                serviceId = _paymentInfo.ChangeDataRecordalServiceID;
                serviceFee = _paymentInfo.ChangeDataRecordalServiceFee;
                break;
            case PaymentTypes.Update:
                if (fileType is FileTypes.Design)
                {
                    amount = _paymentInfo.DesignUpdateCost;
                    serviceId = _paymentInfo.DesignUpdateID;
                    serviceFee = _paymentInfo.DesignUpdateServiceFee;

                    break;
                }
                if (fileType is FileTypes.Patent)
                {
                    if (string.Equals(patentChangeType, "TitleOfInvention", StringComparison.CurrentCultureIgnoreCase))
                    {
                        amount = _paymentInfo.PatentTitleUpdateCost;
                        serviceId = _paymentInfo.PatentTitleUpdateID;
                        serviceFee = _paymentInfo.PatentTitleUpdateServiceFee;
                        break;
                    }

                    if (ConstantValues.IsPropertyAttachment(patentChangeType))
                    {
                        amount = _paymentInfo.PatentAttachmentUpdateCost;
                        serviceId = _paymentInfo.PatentAttachmentUpdateID;
                        serviceFee = _paymentInfo.PatentAttachmentUpdateServiceFee;
                        break;
                    }

                    if (!string.Equals(patentChangeType, "TitleOfInvention",
                            StringComparison.CurrentCultureIgnoreCase) &&
                        !ConstantValues.IsPropertyAttachment(patentChangeType))
                    {
                        amount = _paymentInfo.PatentOtherUpdateCost;
                        serviceId = _paymentInfo.PatentOtherUpdateID;
                        serviceFee = _paymentInfo.PatentOtherUpdateServiceFee;
                        break;
                    }
                }

                if (fileType is FileTypes.TradeMark)
                {
                    if (patentChangeType == "applicants")
                    {
                        amount = _paymentInfo.TrademarkApplicantUpdateCost;
                        serviceId = _paymentInfo.TrademarkApplicantUpdateID;
                        serviceFee = _paymentInfo.TrademarkApplicantUpdateServiceFee;
                    }

                    else
                    {
                        amount = _paymentInfo.TrademarkOtherUpdateCost;
                        serviceId = _paymentInfo.TrademarkOtherUpdateID;
                        serviceFee = _paymentInfo.TrademarkOtherUpdateServiceFee;
                    }
                }

                break;
            case PaymentTypes.LicenseRenew:
                if (fileType is FileTypes.Patent)
                {
                    amount = _paymentInfo.PatentRenewCost;
                    serviceId = _paymentInfo.PatentRenewID;
                    serviceFee = _paymentInfo.PatentRenewServiceFee;

                }

                else if (fileType is FileTypes.Design)
                {
                    if (designType is DesignTypes.NonTextile)
                    {
                        amount = _paymentInfo.DesignNonTextileRenewCost;
                        serviceId = _paymentInfo.DesignNonTextileRenewID;
                        serviceFee = _paymentInfo.DesignNonTextileRenewServiceFee;

                    }
                    else
                    {
                        amount = _paymentInfo.DesignTextileRenewCost;
                        serviceId = _paymentInfo.DesignTextileRenewID;
                        serviceFee = _paymentInfo.DesignTextileRenewServiceFee;

                    }
                }

                else if (fileType is FileTypes.TradeMark)
                {
                    amount = _paymentInfo.TrademarkRenewalFee;
                    serviceId = _paymentInfo.TrademarkRenewalID;
                    serviceFee = _paymentInfo.MergerServiceFee;
                }

                break;
            case PaymentTypes.statusCheck:
                amount = _paymentInfo.StatusCost;
                serviceId = _paymentInfo.StatusServiceId;
                serviceFee = _paymentInfo.StatusServiceFee;
                break;
            case PaymentTypes.LateTrademarkRenewal:
                amount = _paymentInfo.LateTrademarkRenewalCost;
                serviceId = _paymentInfo.LateTrademarkRenewalID;
                serviceFee = _paymentInfo.LateTrademarkRenewalServiceFee;
                break;
            case PaymentTypes.ClericalUpdate:
                amount = _paymentInfo?.ClericalUpdateCost;
                serviceId = _paymentInfo.ClericalUpdateServiceID;
                serviceFee = _paymentInfo.ClericalUpdateServiceFee;
                break;
            case PaymentTypes.DesignClericalUpdate:
                amount = _paymentInfo.DesignClericalUpdateCost;
                serviceId = _paymentInfo.DesignClericalUpdateServiceID;
                serviceFee = _paymentInfo.DesignClericalUpdateServiceFee;
                break;
            case PaymentTypes.StatusSearch:
                amount = _paymentInfo.StatusSearchCost;
                serviceId = _paymentInfo.StatusSearchServiceId;
                serviceFee = _paymentInfo.StatusSearchServiceFee;
                break;
            case PaymentTypes.NonConventional:
                amount = _paymentInfo.PatentCreationNonConventionalCost;
                serviceId = _paymentInfo.PatentCreationNonConventionalID;
                serviceFee = _paymentInfo.PatentCreationNonConventionalServiceFee;
                break;
            case PaymentTypes.PatentClericalUpdate:
                amount = _paymentInfo.PatentClericalUpdateCost;
                serviceId = _paymentInfo.PatentClericalUpdateServiceID;
                serviceFee = _paymentInfo.PatentClericalUpdateServiceFee;
                break;
            case PaymentTypes.PatentLateRenewal:
                amount = _paymentInfo.PatentLateRenewalCost;
                serviceId = _paymentInfo.PatentLateRenewalServiceID;
                serviceFee = _paymentInfo.PatentLateRenewalServiceFee;
                break;
            case PaymentTypes.Opposition:
                amount = _paymentInfo.OppositionCost;
                serviceId = _paymentInfo.OppositionServiceID;
                serviceFee = _paymentInfo.OppositionServiceFee;
                break;
            case PaymentTypes.CounterStatement:
                amount = _paymentInfo.CounterStatementCost;
                serviceId = _paymentInfo.CounterStatementServiceID;
                serviceFee = _paymentInfo.CounterStatementServiceFee;
                break;
            case PaymentTypes.StatutoryDeclaration:
                amount = _paymentInfo.StatutoryDeclarationCost ?? _paymentInfo.CounterStatementCost;
                serviceId = _paymentInfo.StatutoryDeclarationServiceID ?? _paymentInfo.CounterStatementServiceID;
                serviceFee = _paymentInfo.StatutoryDeclarationServiceFee ?? _paymentInfo.CounterStatementServiceFee;
                break;
            case PaymentTypes.OppositionWithdrawal:
                amount = _paymentInfo.OppositionWithdrawalCost ?? "7000";
                serviceId = _paymentInfo.OppositionWithdrawalServiceID ?? _paymentInfo.StatutoryDeclarationServiceID;
                serviceFee = _paymentInfo.OppositionWithdrawalServiceFee ?? "3500";
                break;
            case PaymentTypes.PublicationStatusUpdate:
                amount = _paymentInfo.PublicationStatusUpdateCost;
                serviceId = _paymentInfo.PublicationStatusUpdateServiceID;
                serviceFee = _paymentInfo.PublicationStatusUpdateServiceFee;
                break;
            case PaymentTypes.FileWithdrawal:
                amount = _paymentInfo.WithdrawalCost;
                serviceId = _paymentInfo.WithdrawalServiceID;
                serviceFee = _paymentInfo.WithdrawalServiceFee;
                break;
            case PaymentTypes.Appeal:
                amount += _paymentInfo.AppealCost;
                serviceId = _paymentInfo.AppealServiceID;
                serviceFee = _paymentInfo.AppealServiceFee;
                break;
            case PaymentTypes.PatentAssignment:
                amount = _paymentInfo.PatentAssignmentCost;
                serviceId = _paymentInfo.PatentAssignmentServiceID;
                serviceFee = _paymentInfo.PatentAssignmentServiceFee;
                break;
            case PaymentTypes.PatentLicense:
                amount = _paymentInfo.PatentLicenseCost;
                serviceId = _paymentInfo.PatentLicenseServiceID;
                serviceFee = _paymentInfo.PatentLicenseServiceFee;
                break;
            case PaymentTypes.PatentMortgage:
                amount = _paymentInfo.PatentMortgageCost;
                serviceId = _paymentInfo.PatentMortgageServiceID;
                serviceFee = _paymentInfo.PatentMortgageServiceFee;
                break;
            case PaymentTypes.PatentCtc:
                amount = _paymentInfo.PatentCtcCost;
                serviceId = _paymentInfo.PatentCtcServiceID;
                serviceFee = _paymentInfo.PatentCtcServiceFee;
                break;
            case PaymentTypes.PatentAmendment:
                amount = _paymentInfo.PatentAmendmentCost;
                serviceId = _paymentInfo.PatentAmendmentServiceID;
                serviceFee = _paymentInfo.PatentAmendmentServiceFee;
                break;
            case PaymentTypes.PatentMerger:
                amount = _paymentInfo.PatentMergerCost;
                serviceId = _paymentInfo.PatentMergerServiceID;
                serviceFee = _paymentInfo.PatentMergerServiceFee;
                break;
            case PaymentTypes.DesignAssignment:
                amount = _paymentInfo.DesignAssignmentCost;
                serviceId = _paymentInfo.DesignAssignmentServiceID;
                serviceFee = _paymentInfo.DesignAssignmentServiceFee;
                break;
             case PaymentTypes.DesignLicense:
                amount = _paymentInfo.DesignLicenseCost;
                serviceId = _paymentInfo.DesignLicenseServiceID;
                serviceFee = _paymentInfo.DesignLicenseServiceFee;
                break;
             case PaymentTypes.DesignMerger:
                amount = _paymentInfo.DesignMergerCost;
                serviceId = _paymentInfo.DesignMergerServiceID;
                serviceFee = _paymentInfo.DesignMergerServiceFee;
                break;
             case PaymentTypes.DesignMortgage:
                amount = _paymentInfo.DesignMortgageCost;
                serviceId = _paymentInfo.DesignMortgageServiceID;
                serviceFee = _paymentInfo.DesignMortgageServiceFee;
                break;
             case PaymentTypes.DesignCtc:
                amount = _paymentInfo.DesignCtcCost;
                serviceId = _paymentInfo.DesignCtcServiceID;
                serviceFee = _paymentInfo.DesignCtcServiceFee;
                break;
             case PaymentTypes.TrademarkCtc:
                amount = _paymentInfo.TrademarkCtcCost;
                serviceId = _paymentInfo.TrademarkCtcServiceID;
                serviceFee = _paymentInfo.TrademarkCtcServiceFee;
                break;
             case PaymentTypes.DesignAmendment:
                amount = _paymentInfo.DesignAmendmentCost;
                serviceId = _paymentInfo.DesignAmendmentServiceID;
                serviceFee = _paymentInfo.DesignAmendmentServiceFee;
                break;
            case PaymentTypes.Reclassification:
                amount = _paymentInfo.ReclassificationCost;
                serviceFee = _paymentInfo.ReclassificationServiceFee;
                serviceId = _paymentInfo.ReclassificationServiceID;
                break;
            case PaymentTypes.FileRestoration:
                amount = _paymentInfo.TrademarkRestorationCost;
                serviceId = _paymentInfo.TrademarkRestorationServiceId;
                serviceFee = _paymentInfo.TrademarkRestorationServiceFee;
                break;
            case PaymentTypes.TrademarkAmendment:
                amount = _paymentInfo.TrademarkAmendmentCost;
                serviceId = _paymentInfo.TrademarkAmendmentServiceID;
                serviceFee = _paymentInfo.TrademarkAmendmentServiceFee;
                break;
        }

        return (amount, serviceId, serviceFee);
    }

    public async Task<(string?, string)> GenerateOppositionID(PaymentTypes type, string description, string name, string email, string number)
    {
        var details=GetCost(type, FileTypes.TradeMark, "");
        var rrr=await GenerateRemitaPaymentId(details.Item1, details.Item3,details.Item2,description, name, email, number);
        return (rrr, details.Item1);
    }
    
    public async Task<string?> GenerateRemitaPaymentId(string total, string serviceFee,string serviceId, string description, 
        string applicantName, string applicantEmail, string applicantNumber) {
        if (string.IsNullOrWhiteSpace(total) || string.IsNullOrWhiteSpace(serviceFee))
        {
            throw new ArgumentException("Total or Service Fee cannot be null or empty.");
        }

        if (!int.TryParse(total, out int totalAmount) || !int.TryParse(serviceFee, out int serviceFeeAmount))
        {
            throw new ArgumentException("Total or Service Fee must be valid integers.");
        }
        var _client = new HttpClient();
             var orderId =$"IPONMWD{DateTime.Now.Ticks}";
             // var serviceId = "4019135160";
             var merchantId = "6230040240";
             var apiKey = "192753";
        using StringContent jsonContent = new(
                 JsonSerializer.Serialize(new
                 {
                     serviceTypeId= serviceId,
                     amount= total,
                     orderId,
                     payerName= applicantName,
                     payerEmail= applicantEmail,
                     payerPhone= applicantNumber,
                     description,
                     lineItems= new []
                     {
                         new {
                             lineItemsId= "itemid1",
                             beneficiaryName= "Federal Ministry of Commerce",
                             beneficiaryAccount= "0020110961047",
                             bankCode= "000",
                             beneficiaryAmount= (int.Parse(total) - int.Parse(serviceFee)).ToString(),
                             deductFeeFrom= "1",
                         },
                         new {
                             lineItemsId= "itemid2",
                             beneficiaryName= "Einao Solutions",
                             beneficiaryAccount= "1013590643",
                             bankCode= "057",
                             beneficiaryAmount= serviceFee,
                             deductFeeFrom= "0",
                         }
                     }
                 }),
                 Encoding.UTF8,
                 "application/json");
             _client = new HttpClient();
             var test=merchantId + serviceId +orderId+ total + apiKey;
             var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
             var convertedHash=Convert.ToHexString(apiHash).ToLower();
             Console.WriteLine(convertedHash);
             var request = new HttpRequestMessage(HttpMethod.Post,
                 "https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/merchant/api/paymentinit");
             request.Headers.TryAddWithoutValidation("Authorization",$"remitaConsumerKey={merchantId},remitaConsumerToken={convertedHash}");
             request.Content = jsonContent;
             var response = await _client.SendAsync(request);
             var dataMod = await response.Content.ReadAsStringAsync();
             Console.WriteLine($"[Remita RAW] status={response.StatusCode} body={dataMod}");
             try
             {
                 int startIndex = dataMod.IndexOf("{");
                 int stopIndex = dataMod.LastIndexOf("}") + 1;
                 var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                     dataMod.Substring(startIndex: startIndex, length: stopIndex - startIndex));
                 Console.WriteLine($"[Remita DICT] {JsonSerializer.Serialize(dict)}");
                 string rrr = dict["RRR"].ToString();
                 return rrr;
             }
             catch (Exception e)
             {
                 Console.WriteLine($"[Remita PARSE ERROR] {e.Message} | raw={dataMod}");
                 return null;
             }
             {
                 return null;
             }
    }

    public async Task<string?> GeneratePublicationStatusUpdateRemitaPaymentId(string total, string serviceFee, string serviceId, string description,
    string applicantName, string applicantEmail, string applicantNumber)
    {
        if (string.IsNullOrWhiteSpace(total) || string.IsNullOrWhiteSpace(serviceFee))
        {
            throw new ArgumentException("Total or Service Fee cannot be null or empty.");
        }

        if (!int.TryParse(total, out int totalAmount) || !int.TryParse(serviceFee, out int serviceFeeAmount))
        {
            throw new ArgumentException("Total or Service Fee must be valid integers.");
        }
        var _client = new HttpClient();
        var orderId = $"IPONMWD{DateTime.Now.Ticks}";
        // var serviceId = "4019135160";
        var merchantId = "6230040240";
        var apiKey = "192753";
        using StringContent jsonContent = new(
                 JsonSerializer.Serialize(new
                 {
                     serviceTypeId = serviceId,
                     amount = total,
                     orderId,
                     payerName = applicantName,
                     payerEmail = applicantEmail,
                     payerPhone = applicantNumber,
                     description,
                     lineItems = new[]
                     {
                         new {
                             lineItemsId= "itemid1",
                             beneficiaryName= "Einao Solutions",
                             beneficiaryAccount= "1013590643",
                             bankCode= "057",
                             //beneficiaryName= "Federal Ministry of Commerce",
                             //beneficiaryAccount= "0020110961047",
                             //bankCode= "000",
                             beneficiaryAmount= (int.Parse(total) - int.Parse(serviceFee)).ToString(),
                             deductFeeFrom= "1",
                         },
                         new {
                             lineItemsId= "itemid2",
                             beneficiaryName= "Einao Solutions",
                             beneficiaryAccount= "1013590643",
                             bankCode= "057",
                             beneficiaryAmount= serviceFee,
                             deductFeeFrom= "0",
                         }
                     }
                 }),
                 Encoding.UTF8,
                 "application/json");
        _client = new HttpClient();
        var test = merchantId + serviceId + orderId + total + apiKey;
        var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
        var convertedHash = Convert.ToHexString(apiHash).ToLower();
        Console.WriteLine(convertedHash);
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/merchant/api/paymentinit");
        request.Headers.TryAddWithoutValidation("Authorization", $"remitaConsumerKey={merchantId},remitaConsumerToken={convertedHash}");
        request.Content = jsonContent;
        var response = await _client.SendAsync(request);
        var dataMod = await response.Content.ReadAsStringAsync();
        Console.WriteLine(dataMod);
        try
        {
            int startIndex = dataMod.IndexOf("{");
            int stopIndex = dataMod.IndexOf("}") + 1;
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                dataMod.Substring(startIndex: startIndex, length: stopIndex - startIndex));
            Console.WriteLine(dict);
            string rrr = dict["RRR"].ToString();
            return rrr;
        }
        catch (Exception e)
        {
            return null;
        }
    }


    public FinanceHistory GenerateHistory(
            string reason,
            string country,
            string applicationID,
            string fileId,
            RemitaResponseClass remitaResonse,
            FileTypes Type,
            DesignTypes? DesignType=null,
            PatentTypes? PatentType=null,
            TradeMarkType? TradeMarkType=null,
            int? TradeMarkClass=null
        )
    {
        return new FinanceHistory()
        {
            total = remitaResonse.amount??0.0,
            reason = reason,
            country = country,
            techFee = remitaResonse.lineItems[1].beneficiaryAmount,
            ministryFee = remitaResonse.lineItems[0].beneficiaryAmount,
            date = DateTime.Parse(remitaResonse.paymentDate??DateTime.MinValue.ToString()),
            applicationID = applicationID,
            fileId =  fileId,
            DesignType =  DesignType,
            PatentType =  PatentType,
            Type =  Type,
            TradeMarkType =  TradeMarkType,
            TradeMarkClass =  TradeMarkClass,
            remitaResonse =  remitaResonse,
        };
    }

    public async Task<RemitaResponseClass?> GetDetailsByRRR(string rrr)
    {
        if (string.IsNullOrWhiteSpace(rrr))
        {
            _log.LogWarning("GetDetailsByRRR called with an empty rrr");
            return null;
        }

        const string merchantId = "6230040240";
        const string apiKey = "192753";
        var test = rrr + apiKey + merchantId;
        var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
        var hash = Convert.ToHexString(apiHash).ToLower();
        var transactionStatusUrl =
            $"https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/{merchantId}/{rrr}/{hash}/status.reg";

        _log.LogInformation("Fetching Remita payment details by rrr: {Rrr}", rrr);

        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, transactionStatusUrl);
            request.Headers.TryAddWithoutValidation("Authorization",
                $"remitaConsumerKey={merchantId},remitaConsumerToken={hash}");

            using var response = await client.SendAsync(request);
            var dataMod = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Remita RRR status request failed for rrr {Rrr}. StatusCode: {StatusCode}. Response: {Response}",
                    rrr, (int)response.StatusCode, dataMod);
                return null;
            }

            var result = JsonSerializer.Deserialize<RemitaResponseClass>(dataMod, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                _log.LogWarning("Remita returned an empty payload for rrr {Rrr}", rrr);
                return null;
            }

            result.paymentDate = FormatPaymentDate(result.paymentDate);

            _log.LogInformation("Successfully fetched Remita details for rrr {Rrr}", rrr);
            return result;
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Failed to deserialize Remita response for rrr {Rrr}", rrr);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "HTTP error while fetching Remita details for rrr {Rrr}", rrr);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "Request timed out while fetching Remita details for rrr {Rrr}", rrr);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error while fetching Remita details for rrr {Rrr}", rrr);
            return null;
        }
    }

    public async Task<RemitaResponseClass?> GetDetailsByOrderId(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            _log.LogWarning("GetDetailsByOrderId called with an empty orderId");
            return null;
        }

        const string merchantId = "6230040240";
        const string apiKey = "192753";
        var test = orderId + apiKey + merchantId;
        var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
        var hash = Convert.ToHexString(apiHash).ToLower();
        var transactionStatusUrl =
            $"https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/{merchantId}/{orderId}/{hash}/orderstatus.reg";

        _log.LogInformation("Fetching Remita payment details by orderId: {OrderId}", orderId);

        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, transactionStatusUrl);
            request.Headers.TryAddWithoutValidation("Authorization",
                $"remitaConsumerKey={merchantId},remitaConsumerToken={hash}");

            using var response = await client.SendAsync(request);
            var dataMod = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Remita order status request failed for orderId {OrderId}. StatusCode: {StatusCode}. Response: {Response}",
                    orderId, (int)response.StatusCode, dataMod);
                return null;
            }

            var result = JsonSerializer.Deserialize<RemitaResponseClass>(dataMod, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                _log.LogWarning("Remita returned an empty payload for orderId {OrderId}", orderId);
                return null;
            }

            result.paymentDate = FormatPaymentDate(result.paymentDate);

            _log.LogInformation("Successfully fetched Remita details for orderId {OrderId}", orderId);
            return result;
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Failed to deserialize Remita response for orderId {OrderId}", orderId);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log.LogError(ex, "HTTP error while fetching Remita details for orderId {OrderId}", orderId);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "Request timed out while fetching Remita details for orderId {OrderId}", orderId);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error while fetching Remita details for orderId {OrderId}", orderId);
            return null;
        }
    }

    private static string? FormatPaymentDate(string? paymentDate)
    {
        if (string.IsNullOrWhiteSpace(paymentDate))
        {
            return paymentDate;
        }

        var inputFormats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.F",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.fff"
        };

        if (DateTime.TryParseExact(paymentDate, inputFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedDate))
        {
            return parsedDate.ToString("dd MMMM, yyyy", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(paymentDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
        {
            return parsedDate.ToString("dd MMMM, yyyy", CultureInfo.InvariantCulture);
        }

        return paymentDate;
    }


}

