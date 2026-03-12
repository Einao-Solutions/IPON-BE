using patentdesign.Models;
using patentdesign.Utils;
using System.Text.Json.Serialization;

namespace patentdesign.Dtos.Request
{
    public class PatentAmendmentDto
    {
        public string FileId { get; set; }

        [JsonConverter(typeof(StringToPatentAmendmentTypeConverter))]
        public PatentAmendmentTypes UpdateType { get; set; }

        public FileTypes FileType { get; set; }
        public string PaymentRRR { get; set; }
        public string Rrr => PaymentRRR;

     
        public List<string>? ApplicantNames { get; set; } 
        public List<string>? OldApplicantNames { get; set; } 

        public List<string>? ApplicantAddresses { get; set; } 
        public List<string>? ApplicantEmails { get; set; }    
        public List<string>? ApplicantPhones { get; set; }    
        public List<string>? ApplicantNationalities { get; set; } 
        public List<string>? ApplicantStates { get; set; }   
        public List<string>? ApplicantCities { get; set; }    

        public string? FileTitle { get; set; }         
        public string? PatentAbstract { get; set; }  
        public string? PatentApplicationType { get; set; } 

        public string? CorrespondenceName { get; set; }      
        public string? CorrespondenceAddress { get; set; }  
        public string? CorrespondencePhone { get; set; }   
        public string? CorrespondenceEmail { get; set; }
        public string? CorrespondenceState { get; set; }       
        public string? CorrespondenceNationality { get; set; }

        public List<PriorityInfo>? FirstPriorityInfo { get; set; } 
        public List<PriorityInfo>? PriorityInfo { get; set; }   

        public List<ApplicantInfo>? NewInventors { get; set; } 

        public List<ApplicantInfo>? EditedApplicants { get; set; }  
        public List<ApplicantInfo>? NewApplicants { get; set; }    
        public List<string>? RemoveApplicantIds { get; set; }      

        public DateTime? AmendmentRequestDate { get; set; }
        public DateTime? AmendmentDate { get; set; }
    }
}
