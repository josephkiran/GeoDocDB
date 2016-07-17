using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeoDocDB
{
    class ReadCSVcs
    {
        public static List<GPXData> GetGPXDataFromCSVFile(string csv_file_path)
        {
            List<GPXData> listGPX = new List<GPXData>();

            try
            {
                using (TextFieldParser csvReader = new TextFieldParser(csv_file_path))
                {
                    csvReader.SetDelimiters(new string[] { "," });
                    csvReader.HasFieldsEnclosedInQuotes = false;
                    ////read column names
                    //string[] colFields = csvReader.ReadFields();
                    //foreach (string column in colFields)
                    //{
                    //    DataColumn datecolumn = new DataColumn(column);
                    //    datecolumn.AllowDBNull = true;
                    //    csvData.Columns.Add(datecolumn);
                    //}
                    GPXData dpoint;
                    double tempfloat1;
                    double tempfloat2;
                    while (!csvReader.EndOfData)
                    {
                        dpoint = new GPXData();
                      
                        string[] fieldData = csvReader.ReadFields();
                        if(double.TryParse(fieldData[0],out tempfloat1) && double.TryParse(fieldData[1], out tempfloat2))
                        {
                            dpoint.StartPoint = new Microsoft.Azure.Documents.Spatial.Point(tempfloat2, tempfloat1);
                        }
                        if (double.TryParse(fieldData[2], out tempfloat1) && double.TryParse(fieldData[3], out tempfloat2))
                        {
                            dpoint.EndPoint = new Microsoft.Azure.Documents.Spatial.Point(tempfloat2, tempfloat1);
                        }

                        dpoint.MidPoint = Utils.midPoint(dpoint.StartPoint, dpoint.EndPoint);

                        if(double.TryParse(fieldData[4], out tempfloat1))
                        {
                            dpoint.StdDevRoll = tempfloat1;
                        }
                        if (double.TryParse(fieldData[5], out tempfloat1))
                        {
                            dpoint.StdDevPitch = tempfloat1;
                        }
                        if (double.TryParse(fieldData[6], out tempfloat1))
                        {
                            dpoint.StdDevYaw = tempfloat1;
                        }

                        if (double.TryParse(fieldData[8], out tempfloat1))
                        {
                            dpoint.Speed = tempfloat1;
                        }

                        DateTime dt = new DateTime();
                        if(DateTime.TryParse(fieldData[10],out dt))
                        {
                            dpoint.Time = dt;
                        }
                        listGPX.Add(dpoint);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return listGPX;
        }
    }
}
