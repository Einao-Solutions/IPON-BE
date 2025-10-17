using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bogus.DataSets;
using Microsoft.Extensions.Options;
using patentdesign.Models;

namespace patentdesign.Utils;

public class PaymentUtils(IOptions<PaymentInfo> remitaPaymentDetails)
{
    private PaymentInfo _paymentInfo = remitaPaymentDetails.Value;

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
            case PaymentTypes.LateRenewal:
                amount = _paymentInfo.LateTrademarkRenewalCost;
                serviceId = _paymentInfo.LateTrademarkRenewalID;
                serviceFee = _paymentInfo.LateTrademarkRenewalServiceFee;
                break;
            case PaymentTypes.ClericalUpdate:
                amount = _paymentInfo?.ClericalUpdateCost;
                serviceId = _paymentInfo.ClericalUpdateServiceID;
                serviceFee = _paymentInfo.ClericalUpdateServiceFee;
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

    public async Task<string?> GenerateFileWithdrawalRemitaPaymentId(string total, string serviceFee, string serviceId, string description,
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
        const string merchantId = "6230040240";
        const string apiKey = "192753";
        var test = rrr + apiKey + merchantId;
        var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
        var hash = Convert.ToHexString(apiHash).ToLower();
        var transactionStatusUrl =
            $"https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/{merchantId}/{rrr}/{hash}/status.reg";
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, transactionStatusUrl);
        request.Headers.TryAddWithoutValidation("Authorization",
            $"remitaConsumerKey={merchantId},remitaConsumerToken={hash}");
        var response = await client.SendAsync(request);
        var dataMod = await response.Content.ReadAsStringAsync();
        RemitaResponseClass? obj = null;
        try
        {
            obj = JsonSerializer.Deserialize<RemitaResponseClass>(dataMod);
        }
        catch (Exception e)
        {
            obj = null;
        }

        return obj;
    }

    public async Task<RemitaResponseClass?> GetDetailsByOrderId(string orderId)
    {
        Console.WriteLine($"Getting details based on orderId, {orderId}");
        const string merchantId = "6230040240";
        const string apiKey = "192753";
        var test = orderId + apiKey + merchantId;
        var apiHash = SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(test));
        var hash = Convert.ToHexString(apiHash).ToLower();
        var transactionStatusUrl =
            $"https://login.remita.net/remita/exapp/api/v1/send/api/echannelsvc/{merchantId}/{orderId}/{hash}/orderstatus.reg";
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, transactionStatusUrl);
        request.Headers.TryAddWithoutValidation("Authorization",
            $"remitaConsumerKey={merchantId},remitaConsumerToken={hash}");
        var response = await client.SendAsync(request);
        var dataMod = await response.Content.ReadAsStringAsync();
        RemitaResponseClass? obj = null;
        try
        {
            obj = JsonSerializer.Deserialize<RemitaResponseClass>(dataMod);
            return obj;
        }
        catch (Exception e)
        {
            return null;
        }
    }

    public (string, string, string?) GetPatentLateRenewalCost()
    {
        return (_paymentInfo.PatentLateRenewalCost, _paymentInfo.PatentLateRenewalServiceFee, _paymentInfo.PatentLateRenewalServiceID);
    }
}

