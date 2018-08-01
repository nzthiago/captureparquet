
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Newtonsoft.Json.Linq;
using Microsoft.WindowsAzure.Storage.Blob;
using Avro;
using Avro.Generic;
using Parquet;
using Parquet.Data;
using Microsoft.Azure.EventHubs;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Microsoft.CaptureParquet
{
    public static class CaptureTrigger
    {
        [FunctionName("CaptureTrigger")]
        public static void Run([EventGridTrigger]EventGridEvent eventGridEvent, 
                                         [Blob("{data.fileUrl}", FileAccess.Read, Connection = "feedstore_STORAGE")]CloudBlockBlob inputBlob, 
                                         [Blob("parquet/{rand-guid}.parquet", FileAccess.Write, Connection = "feedstore_STORAGE")]CloudBlockBlob outputBlob, 
                                         TraceWriter log)
        {
            log.Info(eventGridEvent.ToString());
            JObject dataObject = eventGridEvent.Data as JObject;
            var eventData = dataObject.ToObject<EventHubCaptureFileCreatedEventData>();

            log.Info(eventData.EventCount.ToString() + " events in record");
            if(eventData.EventCount > 0)
            {
                log.Info("Got a real file");
                Stream avroStream = inputBlob.OpenReadAsync().Result;
                CloudBlobStream parquetStream = outputBlob.OpenWriteAsync().Result;
                CreateParquetFile(avroStream, parquetStream);
                parquetStream.Close();
            }
        }

        public static void CreateParquetFile(Stream inStream, Stream outStream)
        {
            using (var writer = new ParquetWriter(outStream))
            {
                DataSet ds = null;
                int recordCount = 0;
                foreach (var data in ReadFile(inStream))
                {
                    if (recordCount == 0)
                    {
                        List<Parquet.Data.Field> fields = new List<Parquet.Data.Field>();
                        foreach (var prop in data.Properties)
                        {
                            fields.Add(new DataField(prop.Key, prop.Value.GetType()));
                        }
                        foreach (var prop in data.SystemProperties)
                        {
                            fields.Add(new DataField(prop.Key, prop.Value.GetType()));
                        }
                        fields.Add(new DataField<byte[]>("Body"));
                        ds = new DataSet(fields.ToArray());
                    }
                    List<Object> values = new List<object>();
                    values.AddRange(data.Properties.Values);
                    values.AddRange(data.SystemProperties.Values);
                    values.Add(data.Body.ToArray());
                    ds.Add(values.ToArray());
                    recordCount++;
                }
                writer.Write(ds);
            }
        }

        static IEnumerable<EventData> ReadFile(Stream stream)
        {
            var t = typeof(EventData);
            var sysPropType = typeof(Microsoft.Azure.EventHubs.EventData.SystemPropertiesCollection);

            var reader = Avro.File.DataFileReader<GenericRecord>.OpenReader(stream);
            Dictionary<string, List<object>> dictionary = new Dictionary<string, List<object>>();
            while (reader.HasNext())
            {
                EventData result = null;
                Object body;
                var data = reader.Next();
                if (data.TryGetValue("Body", out body))
                {
                    result = new EventData(body as byte[]);
                    t.GetProperty("SystemProperties").SetValue(result, Activator.CreateInstance(sysPropType, true));
                }
                Object userProperties;
                if (data.TryGetValue("Properties", out userProperties))
                {
                    Dictionary<String, Object> properties = userProperties as Dictionary<String, Object>;
                    foreach(var property in properties)
                    {
                        result.Properties.Add(property.Key, property.Value);
                    }
                }
                Object sysProperties;
                if (data.TryGetValue("SystemProperties", out sysProperties))
                {
                    Dictionary<String, Object> properties = sysProperties as Dictionary<String, Object>;
                    foreach (var property in properties)
                    {
                        result.SystemProperties[property.Key] = property.Value;
                    }
                }
                IEnumerator<Avro.Field> enu = data.Schema.GetEnumerator();
                while (enu.MoveNext())
                {
                    if (enu.Current.Name == "Body" || enu.Current.Name == "SystemProperties" || enu.Current.Name == "Properties")
                        continue;
                    Object prop;
                    if (data.TryGetValue(enu.Current.Name, out prop))
                    {
                        result.SystemProperties[enu.Current.Name] = prop;
                    }
                }
                yield return result;   
            }
        }
    }
}