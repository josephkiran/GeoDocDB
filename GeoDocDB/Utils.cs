using Microsoft.Azure.Documents.Spatial;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace GeoDocDB
{
    public static class Utils
    {
        private static double deg2rad(double deg)
        {
            return (deg * Math.PI / 180.0);
        }

        private static double rad2deg(double rad)
        {
            return (rad / Math.PI * 180.0);
        }
        public static double distance(Point p1, Point p2, char unit)
        {
            
            double lat1 = p1.Position.Latitude;
            double lon1 = p1.Position.Longitude;
            double lat2 = p2.Position.Latitude;
            double lon2 = p2.Position.Longitude;

            double theta = lon1 - lon2;
            double dist = Math.Sin(deg2rad(lat1)) * Math.Sin(deg2rad(lat2)) + Math.Cos(deg2rad(lat1)) * Math.Cos(deg2rad(lat2)) * Math.Cos(deg2rad(theta));
            dist = Math.Acos(dist);
            dist = rad2deg(dist);
            dist = dist * 60 * 1.1515;
            if (unit == 'K')
            {
                dist = dist * 1.609344;
            }
            else if (unit == 'N')
            {
                dist = dist * 0.8684;
            }
            return (dist);
        }

        public static Point midPoint(Point p1, Point p2)
        {
            double lat1 = p1.Position.Latitude;
            double lon1 = p1.Position.Longitude;
            double lat2 = p2.Position.Latitude;
            double lon2 = p2.Position.Longitude;

            double dLon = deg2rad(lon2 - lon1);

            //convert to radians
            lat1 = deg2rad(lat1);
            lat2 = deg2rad(lat2);
            lon1 = deg2rad(lon1);

            double Bx = Math.Cos(lat2) * Math.Cos(dLon);
            double By = Math.Cos(lat2) * Math.Sin(dLon);
            double lat3 = Math.Atan2(Math.Sin(lat1) + Math.Sin(lat2), Math.Sqrt((Math.Cos(lat1) + Bx) * (Math.Cos(lat1) + Bx) + By * By));
            double lon3 = lon1 + Math.Atan2(By, Math.Cos(lat1) + Bx);

            return new Point(rad2deg(lon3), rad2deg(lat3));

        }

        public static SnappedPoints GetSnapedRoad(string s1)
        {
            string url = @"https://roads.googleapis.com/v1/snapToRoads?path=" + s1 +  @"&interpolate=true&key=AIzaSyDPi40I9WBTnNBAkf9bHR7r3-3IBs4X2xI";
            string s = string.Empty;
            using (WebClient client = new WebClient())
            {
                 s = client.DownloadString(url);
            }
            var jsonDto = JsonConvert.DeserializeObject<SnappedPoints>(s);
            //List<SnapSegments> list = JsonConvert.DeserializeObject<List<SnapSegments>>(s);
            for (int i = 0; i < jsonDto.snappedPoints.Count; i++)
            {
                jsonDto.snappedPoints[i].point = new Point(jsonDto.snappedPoints[i].location.longitude, jsonDto.snappedPoints[i].location.latitude);
            }
            return jsonDto;
        }
    }

    public class Location
    {
        public double latitude { get; set; }
        public double longitude { get; set; }
    }

    public class SnappedPoint
    {
        Location _location = new Location();
        public SnappedPoint()
        {
            point = new Point(_location.longitude, _location.latitude);
        }
        public Point point { get; set; }
        public Location location {
            get { return _location; }
            set {
                _location = value;
                //point = new Point(_location.longitude, _location.latitude);
            }
        }
        public int originalIndex { get; set; }
        public string placeId { get; set; }
       
    }

    public class SnappedPoints
    {
        public List<SnappedPoint> snappedPoints { get; set; }
    }
   

}
