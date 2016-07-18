using Microsoft.Azure.Documents.Spatial;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeoDocDB
{
    public class GPXData
    {
        public GPXData()
        {
            Id = Guid.NewGuid().ToString();
        }

        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        [JsonProperty("startpoint")]
        public Point StartPoint { get; set; }

        [JsonProperty("endpoint")]
        public Point EndPoint { get; set; }

        [JsonProperty("midpoint")]
        public Point MidPoint { get; set; }

        [JsonProperty("snappoint")]
        public Point SnapPoint { get; set; }

        [JsonProperty("x")]
        public double StdDevYaw { get; set; }

        [JsonProperty("y")]
        public double StdDevRoll { get; set; }

        [JsonProperty("z")]
        public double StdDevPitch { get; set; }

        [JsonProperty("roadtype")]
        public RoadType RoadCondition { get; set; }

        [JsonProperty("speed")]
        public double Speed { get; set; }

        [JsonProperty("time")]
        public DateTime Time { get; set; }

        public int SeqNumber { get; set; }

        public string TripID { get; set; }

        public int L0 { get; set; }

        public int L1 { get; set; }

        public int L2 { get; set; }


        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.None);
            //return string.Format("YAW:{0}\nPITCH:{1}\nROLL:{2}\nTYPE:{3}", StdDevYaw.ToString(), StdDevPitch.ToString(), StdDevRoll.ToString(), RoadCondition.ToString());
            //return string.Format("TYPE: {0}", RoadCondition.ToString());
        }

    }

    public enum RoadType
    {
        Good = 0,
        SlightyBumpy = 1,
        Bumpy = 2,
        Worst = 3,
        RandomAction = 4,
        Idle = 99
    }
}
