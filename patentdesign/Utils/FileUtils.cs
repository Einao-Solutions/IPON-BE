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

    public static class TrademarkClassMapper
    {
        private static readonly Dictionary<int, string> Classes = new()
    {
        {1, "Chemicals used in industry, science and photography, as well as in agriculture, horticulture and forestry; unprocessed artificial resins and plastics; fertilizers; fire extinguishing compositions; tempering and soldering preparations; chemical substances for preserving foodstuffs; tanning substances; adhesives used in industry."},
        {2, "Paints, varnishes, lacquers; preservatives against rust and against deterioration of wood; colorants, dyes; raw natural resins; metals in foil and powder form for use in painting, decorating, printing and art."},
        {3, "Bleaching preparations and other substances for laundry use; cleaning, polishing, scouring and abrasive preparations; soaps; perfumery, essential oils, cosmetics, hair lotions; dentifrices."},
        {4, "Industrial oils and greases, wax; lubricants; dust absorbing, wetting and binding compositions; fuels and illuminants; candles and wicks for lighting."},
        {5, "Pharmaceuticals, medical and veterinary preparations; sanitary preparations for medical purposes; dietetic food and substances adapted for medical or veterinary use; baby food; dietary supplements; plasters, materials for dressings; disinfectants; preparations for destroying vermin; fungicides, herbicides."},
        {6, "Common metals and their alloys; metal building materials; transportable buildings of metal; materials of metal for railway tracks; non-electric cables and wires of common metal; metal hardware; pipes and tubes of metal; safes; ores."},
        {7, "Machines, machine tools, power-operated tools; motors and engines except for land vehicles; machine coupling and transmission components; agricultural implements other than hand-operated; incubators for eggs."},
        {8, "Hand tools and implements hand-operated; cutlery; side arms; razors."},
        {9, "Scientific, research, navigation, surveying, photographic, cinematographic, audiovisual, optical, weighing, measuring, signalling, detecting, testing, inspecting, life-saving and teaching apparatus and instruments; computers and software."},
        {10, "Surgical, medical, dental and veterinary apparatus and instruments; artificial limbs, eyes and teeth; orthopedic articles; suture materials; therapeutic and assistive devices for persons with disabilities."},
        {11, "Apparatus and installations for lighting, heating, cooling, steam generating, cooking, drying, ventilating, water supply and sanitary purposes."},
        {12, "Vehicles; apparatus for locomotion by land, air or water."},
        {13, "Firearms; ammunition and projectiles; explosives; fireworks."},
        {14, "Precious metals and their alloys; jewellery; precious and semi-precious stones; horological and chronometric instruments."},
        {15, "Musical instruments."},
        {16, "Paper and cardboard; printed matter; bookbinding material; photographs; stationery and office requisites; artists' materials; paint brushes."},
        {17, "Unprocessed and semi-processed rubber, gutta-percha, gum, asbestos, mica; plastics in extruded form for use in manufacture; packing, stopping and insulating materials."},
        {18, "Leather and imitations of leather; animal skins and hides; luggage and carrying bags; umbrellas and parasols; walking sticks; saddlery."},
        {19, "Materials not of metal for building and construction; rigid pipes not of metal for building; asphalt, pitch and bitumen; transportable buildings not of metal; monuments not of metal."},
        {20, "Furniture, mirrors, picture frames; containers not of metal for storage or transport; bedding except linen."},
        {21, "Household or kitchen utensils and containers; cookware and tableware except forks, knives and spoons; combs and sponges; brushes; glassware, porcelain and earthenware."},
        {22, "Ropes and string; nets; tents and tarpaulins; awnings of textile or synthetic materials; sails; sacks for transport and storage; raw fibrous textile materials."},
        {23, "Yarns and threads for textile use."},
        {24, "Textiles and substitutes for textiles; household linen; curtains of textile or plastic."},
        {25, "Clothing, footwear, headwear."},
        {26, "Lace and embroidery; ribbons and braid; buttons, hooks and eyes; pins and needles; artificial flowers; hair decorations."},
        {27, "Carpets, rugs, mats and matting; linoleum and other materials for covering floors; wall hangings not of textile."},
        {28, "Games, toys and playthings; video game apparatus; gymnastic and sporting articles; decorations for Christmas trees."},
        {29, "Meat, fish, poultry and game; preserved fruits and vegetables; dairy products; edible oils and fats."},
        {30, "Coffee, tea, cocoa; rice; flour and preparations made from cereals; bread, pastry and confectionery; honey; yeast; baking powder; salt; mustard; vinegar; sauces; spices."},
        {31, "Raw and unprocessed agricultural products; live animals; fresh fruits and vegetables; seeds; natural plants and flowers; foodstuffs for animals."},
        {32, "Beers; mineral and aerated waters; fruit beverages and fruit juices; syrups and other preparations for making beverages."},
        {33, "Alcoholic beverages except beers."},
        {34, "Tobacco and tobacco substitutes; smokers’ articles; matches."},
        {35, "Advertising; business management; business administration; office functions."},
        {36, "Insurance; financial affairs; monetary affairs; real estate affairs."},
        {37, "Building construction; repair; installation services."},
        {38, "Telecommunications services."},
        {39, "Transport; packaging and storage of goods; travel arrangement."},
        {40, "Treatment of materials."},
        {41, "Education; providing of training; entertainment; sporting and cultural activities."},
        {42, "Scientific and technological services; research and design relating thereto; design and development of computer hardware and software."},
        {43, "Services for providing food and drink; temporary accommodation."},
        {44, "Medical services; veterinary services; hygienic and beauty care for human beings or animals; agriculture, horticulture and forestry services."},
        {45, "Legal services; security services for the protection of property and individuals; personal and social services rendered by others to meet the needs of individuals."}
    };

        public static string GetDescription(int classNumber)
        {
            return Classes.TryGetValue(classNumber, out var description)
                ? description
                : "Unknown trademark class";
        }
    }
}