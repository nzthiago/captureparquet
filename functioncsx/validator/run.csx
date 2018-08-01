#r "Microsoft.Azure.EventGrid"
#r "Microsoft.WindowsAzure.Storage"

using Microsoft.Azure.EventGrid.Models;
using Microsoft.WindowsAzure.Storage.Blob;

public static void Run(EventGridEvent eventGridEvent, CloudBlockBlob inputBlob, CloudBlockBlob outputBlob, TraceWriter log)
{
    log.Info(eventGridEvent.ToString());
    log.Info(eventGridEvent.Data.ToString());
    outputBlob.StartCopyAsync(inputBlob).Wait();

}
