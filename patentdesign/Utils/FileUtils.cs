using System.Text.Json;
using patentdesign.Models;

namespace patentdesign.Utils;
public class FileUtils
{
    public static Object MapObjToType(string type, dynamic obj)
    {
        switch (type.ToLower())
        {
            case "correspondence":
                return JsonSerializer.Deserialize<CorrespondenceType>(obj, new JsonSerializerOptions(){PropertyNameCaseInsensitive = true});
            case "applicants":
            case "inventors":
            case "designcreators":
                var des=JsonSerializer.Deserialize<List<ApplicantInfo>>(obj, new JsonSerializerOptions(){PropertyNameCaseInsensitive = true});
                return des;
            case "priorityinfo":
                return JsonSerializer.Deserialize<List<PriorityInfo>>(obj, new JsonSerializerOptions(){PropertyNameCaseInsensitive = true});
            case "patentapplicationtype":
                var val = Enum.Parse(typeof(PatentApplicationTypes), obj.ToString());
                return val;
            case "designtype":
                return Enum.Parse(typeof(DesignTypes), obj);
            case "patenttype":
                return Enum.Parse(typeof(PatentTypes), obj);
            case "trademarkclass":
                return int.Parse(obj);
            case "trademarktype":
                return Enum.Parse(typeof(TradeMarkType), obj);
            case "trademarklogo":
                return Enum.Parse(typeof(TradeMarkLogo), obj);
            default:
                return JsonSerializer.Deserialize<string>(obj);
                
                
        }
    }
}