﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PlasmaShared;
using ZkData;

namespace PlasmaDownloader
{
    public interface IChobbylaProgress
    {
        Download Download { get; set; }
        string Status { get; set; }
    }


    public static class ChobbylaHelper
    {
        public static Task<bool> DownloadFile(this PlasmaDownloader downloader, DownloadType type,
            string name,
            IChobbylaProgress progress) => DownloadFile(downloader, name, type, name, progress);

        public static async Task<bool> DownloadFile(this PlasmaDownloader downloader,
            string desc,
            DownloadType type,
            string name,
            IChobbylaProgress progress)
        {
            progress.Status = desc;
            progress.Download = downloader.GetResource(type, name);
            var dlTask = progress.Download?.WaitHandle.AsTask(TimeSpan.FromMinutes(30));
            if (dlTask != null) await dlTask.ConfigureAwait(false);
            if (progress.Download?.IsComplete == false)
            {
                progress.Status = $"Download of {progress.Download.Name} has failed";
                return false;
            }
            return true;
        }

        public static async Task<bool> DownloadUrl(this PlasmaDownloader downloader,
            string desc,
            string url,
            string filePathTarget,
            IChobbylaProgress progress)
        {
            progress.Status = desc;
            var wfd = new WebFileDownload(url, filePathTarget, downloader.SpringPaths.Cache);
            wfd.Start();
            progress.Download = wfd;
            var dlTask = progress.Download?.WaitHandle.AsTask(TimeSpan.FromMinutes(30));
            if (dlTask != null) await dlTask.ConfigureAwait(false);
            if (progress.Download?.IsComplete == false)
            {
                progress.Status = $"Download of {progress.Download.Name} has failed";
                return false;
            }
            return true;
        }


        public static async Task<bool> UpdateMissions(this PlasmaDownloader downloader, IChobbylaProgress progress)
        {
            try
            {
                progress.Status = "Downloading missions";
                var missions = GlobalConst.GetContentService().GetDefaultMissions();

                var missionsFolder = Path.Combine(downloader.SpringPaths.WritableDirectory, "missions");
                if (!Directory.Exists(missionsFolder)) Directory.CreateDirectory(missionsFolder);
                var missionFile = Path.Combine(missionsFolder, "missions.json");

                List<ClientMissionInfo> existing = null;
                if (File.Exists(missionFile))
                    try
                    {
                        existing = JsonConvert.DeserializeObject<List<ClientMissionInfo>>(File.ReadAllText(missionFile));
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning("Error reading mission file {0} : {1}", missionFile, ex);
                    }
                existing = existing ?? new List<ClientMissionInfo>();

                var toDownload =
                    missions.Where(
                            m => !existing.Any(x => (x.MissionID == m.MissionID) && (x.Revision == m.Revision) && (x.DownloadHandle == m.DownloadHandle)))
                        .ToList();

                // download mission files
                foreach (var m in toDownload)
                {
                    if (m.IsScriptMission && (m.Script != null)) m.Script = m.Script.Replace("%MAP%", m.Map);
                    if (!m.IsScriptMission) if (!await downloader.DownloadFile("Downloading mission " + m.DisplayName, DownloadType.MISSION, m.DownloadHandle, progress).ConfigureAwait(false)) return false;
                    if (!await downloader.DownloadUrl("Downloading image", m.ImageUrl, Path.Combine(missionsFolder, $"{m.MissionID}.png"), progress).ConfigureAwait(false)) return false;
                }

                File.WriteAllText(missionFile, JsonConvert.SerializeObject(missions));

                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError("Error updating missions: {0}", ex);
                return false;
            }
        }
    }
}