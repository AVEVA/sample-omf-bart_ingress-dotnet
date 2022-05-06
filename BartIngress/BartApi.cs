using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BartIngress
{
    public static class BartApi
    {
        private static readonly HttpClient _client = new ();

        /// <summary>
        /// Gets and parses the current data from the BART ETD (Estimated Time of Departure) API
        /// </summary>
        /// <param name="key">BART API registration key</param>
        /// <param name="orig">Specifies the origin station abbreviation. The default value, "all", will get all current ETDs.</param>
        /// <param name="dest">Specifies the destination station abbreviation. The default value, "all", will parse all current destination ETDs.</param>
        /// <returns>A dictionary of ETD data keyed by the stream ID</returns>
        internal static Dictionary<string, IEnumerable<BartStationEtd>> GetRealTimeEstimates(string key, string orig = "all", string dest = "all")
        {
            Dictionary<string, IEnumerable<BartStationEtd>> data = new ();
            string etdJson = HttpGet(key, orig);
            JToken etdRoot = JsonConvert.DeserializeObject<JObject>(etdJson)["root"];
            string date = (string)etdRoot["date"];
            string time = (string)etdRoot["time"];
            time = time.Replace("PST", "-8:00", StringComparison.OrdinalIgnoreCase).Replace("PDT", "-7:00", StringComparison.OrdinalIgnoreCase);
            DateTime dateTime = DateTime.ParseExact(date + " " + time, "MM/dd/yyyy hh:mm:ss tt zzz", CultureInfo.InvariantCulture).ToUniversalTime();
            JArray origins = (JArray)etdRoot["station"];
            foreach (JObject origin in origins)
            {
                string origAbbr = (string)origin["abbr"];
                JArray destinations = (JArray)origin["etd"];
                foreach (JObject destination in destinations)
                {
                    string destAbbr = (string)destination["abbreviation"];
                    if (string.Equals(dest, "all", StringComparison.OrdinalIgnoreCase) || string.Equals(dest, destAbbr, StringComparison.OrdinalIgnoreCase))
                    {
                        JObject estimate = (JObject)destination["estimate"][0];
                        BartStationEtd stationEtd = new (dateTime, estimate);
                        string streamId = $"BART_{origAbbr}_{destAbbr}";
                        data.Add(streamId, new BartStationEtd[] { stationEtd });
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// Runs an HTTP Get request against the BART ETD (Estimated Time of Departure) API
        /// <param name="key">BART API registration key</param>
        /// <param name="orig">Specifies the origin station abbreviation. The default value, "all", will get all current ETDs.</param>
        /// </summary>
        private static string HttpGet(string key, string orig = "all")
        {
            Uri uri = new ($"https://api.bart.gov/api/etd.aspx?cmd=etd&orig={orig}&key={key}&json=y");
            using HttpRequestMessage request = new (HttpMethod.Get, uri);

            return Send(request).Result;
        }

        /// <summary>
        /// Send message using HttpRequestMessage
        /// </summary>
        private static async Task<string> Send(HttpRequestMessage request)
        {
            HttpResponseMessage response = await _client.SendAsync(request).ConfigureAwait(false);

            string responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Error sending OMF response code:{response.StatusCode}.  Response {responseString}");
            return responseString;
        }
    }
}
