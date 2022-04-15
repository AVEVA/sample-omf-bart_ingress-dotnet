using System;
using System.Threading;
using BartIngress;
using OSIsoft.Data;
using OSIsoft.Data.Http;
using OSIsoft.Identity;
using Xunit;

namespace BartIngressTests
{
    public class UnitTests
    {
        [Fact]
        public void BartIngressTest()
        {
            Program.LoadConfiguration();

            // Verify timestamp is within last minute
            DateTime verifyTimestamp = DateTime.UtcNow.AddMinutes(-1);

            // Test requires that specific stations are chosen for BartApiOrig and BartApiDest, "all" is not allowed
            string streamId = $"BART_{Program.Settings.BartApiOrig}_{Program.Settings.BartApiDest}";

            try
            {
                Program.RunIngress();

                // Wait for data to be processed by ADH
                Thread.Sleep(5000);

                // Edge Data Store and PI Web API process OMF before sending a response, and will return an error code if there is a problem
                // In this test, the call to RunIngress above will result in an exception if there is a failure on either of those endpoints

                // ADH does not validate OMF before sending a success response, so the test must check that the messages were successful
                using AuthenticationHandler adhAuthenticationHandler = new (Program.Settings.AdhUri, Program.Settings.AdhClientId, Program.Settings.AdhClientSecret);
                SdsService adhSdsService = new (Program.Settings.AdhUri, null, HttpCompressionMethod.GZip, adhAuthenticationHandler);
                ISdsDataService adhDataService = adhSdsService.GetDataService(Program.Settings.AdhTenantId, Program.Settings.AdhNamespaceId);
                BartStationEtd adhValue = adhDataService.GetLastValueAsync<BartStationEtd>(streamId).Result;
                Assert.True(adhValue.TimeStamp > verifyTimestamp);
            }
            finally
            {
                // Delete type and containers
                Program.Cleanup();
            }
        }
    }
}
