using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Models
{
    public class ValorantAgentsResponse
    {
        public int status { get; set; }
        public List<ValorantAgent> data { get; set; }
    }

    public class ValorantAgent
    {
        public string uuid { get; set; }
        public string displayName { get; set; }
        public string description { get; set; }
        public string displayIcon { get; set; }
        public string displayIconSmall { get; set; }
        public string fullPortrait { get; set; }
        public string fullPortraitV2 { get; set; }
        public List<ValorantAbility> abilities { get; set; }
        public ValorantRole role { get; set; }
        public bool isPlayableCharacter { get; set; }
    }

    public class ValorantAbility
    {
        public string slot { get; set; }
        public string displayName { get; set; }
        public string description { get; set; }
        public string displayIcon { get; set; }
    }

    public class ValorantRole
    {
        public string uuid { get; set; }
        public string displayName { get; set; }
        public string description { get; set; }
        public string displayIcon { get; set; }
    }

    /// <summary>
    /// Valorant ¹CÀ¸ª¬ºA
    /// </summary>
    public class ValorantGameSession
    {
        public ValorantAgent CorrectAgent { get; set; }
        public ValorantAbility SelectedAbility { get; set; }
        public bool IsImageMode { get; set; }
        public ulong MessageId { get; set; }
    }

    public class ValorantWeaponResponse
    {
        public int status { get; set; }
        public List<ValorantWeapon> data { get; set; }
    }

    public class ValorantWeapon
    {
        public string uuid { get; set; }
        public string displayName { get; set; }
        public List<Skin> skins { get; set; }
    }

    public class Skin
    {
        public string displayName { get; set; }
        public string displayIcon { get; set; }
    }
    
}
