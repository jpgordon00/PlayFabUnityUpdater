using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using PlayFab.ClientModels;
using PlayFab.CloudScriptModels;
using PlayFab.Helpers;
using PlayFab;
using System.IO;
using LoginResult = PlayFab.ClientModels.LoginResult;
using SimpleJSON;
using System;
using System.Threading.Tasks;
using UnityEngine;
using System.Threading;

/*
        Optional type of asset for a FileManifest object
        JSON stores the assets name in Refs with a JSONNode object.
        IMAGE stores the assets name in Refs with a Sprite 
        SPINE stgores the assets name in Refs with a Spine ruintime obj
    */
    public enum AssetType {
        JSON,
        SPRITE,
        SPINE,
        TEXT
    }

    // list of our content files directed from PlayFab
    public class FileManifest
    {

        // local path to this file
        // usually set to Application.persistantDataPath combined with PartialPath
        public string Path {
            get;
            set;
        }

        // filename including filetype
        public string Filename {
            get;
            set;
        }
        public string URI {
            get;
            set;
        }

        // content-name given 
        public string Name {
            get;
            set;
        }

        // path to download the content from PlayFab CDN
        // format: UPDATE_VERSION/FOLDER_NAME/ASSET_NAME
        public string PartialPath {
            get;
            set;
        }

        /*
            Optional AssetType to load the selected type into Refs 
        */
        public AssetType AssetType {
            get; set;
        }

        public override String ToString() {
            return "{Path=" + Path + "}{Filename=" + Filename + "}{URI=" + URI + "}{PartialPath=" + PartialPath + "{Name=" + Name + "}";
        }

        
    }

    // handles downloading files from PlayFab CDN stored in 'FileManifest'
    // cleans up previous versions and only downloads missing files
    // TODO: listeners
    public class UpdateHandler
    {
        // singleton pattern
        private static UpdateHandler _instance;

        public static UpdateHandler Instance {
            get {
                if (_instance == null) _instance = new UpdateHandler();
                return _instance;
            }
        }

        /* Time in seconds before a download timeout */
        public int Timeout = 2;

        // root path for content/update files
        public string DataPath {
            get {
                return Application.persistentDataPath;
            }
        }

        // name of Azure Function / Cloudscript for polling update
        private string _updateScriptName {
            get {
                return "PollUpdaterContent";
            }
        }

        // true if 'Update' has been invoked and not failed or succeeded
        // ( update in progress ) 
        private bool _isUpdating = false;

        public bool IsUpdating {
            get {
                return _isUpdating;
            }
        }

        // current version given by CloudScript
        private string _currentVersion;
        public string CurrentVersion {
            get {
                return _currentVersion;
            }
        }

        // max amount of  to wait for entire procedure
        // before triggering an update failure
        private int _maxTime = 6;

        // wait time before checking if a download can occur
        private int _delayTime = 2;

        // time that update was called last
        private int _updateStartTime = 0;

        // last elapsed time if finished or 0
        private float _elapsedTime = 0;

        // current elapsed time or last elapsed time
        // 0 if not downloaded yet
        public float ElapsedTime {
            get {
                if (_elapsedTime == 0) return _elapsedTime;
                return (_elapsedTime = (DateTime.Now.Second - _updateStartTime));
            }
        }

        // allows the download of multiple files with handy callback events
        private GroupDownloader _downloader;

        public GroupDownloader Downloader {
            get {
                return _downloader;
            }
        }

        // number of expected files to download, returned by CloudScript
        private int _expectedFileCount = 0;

        public int ExpectedFileCount {
            get {
                return _expectedFileCount;
            }
        }

        // list of resources to downlaod from PlayFab CDN
        // and internal paths for resource files specified by Cloudscript
        private List < FileManifest > _fileManifest = new List < FileManifest > ();

        public List < FileManifest > FileManifest {
            get {
                return _fileManifest;
            }
        }

        // get a FileManifest by name or null
        public FileManifest FindManifestByName(string name) {
            foreach(var manifest in _fileManifest) {
                if (manifest.Name == name) return manifest;
            }
            return null;
        }

        // get a file path by name or null
        public string FindPathByName(string name) {
            foreach(var manifest in _fileManifest) {
                if (manifest.Name == name) return manifest.Path;
            }
            return null;
        }

        // resets this class
        // param true to reset file related objects
        private void Reset(bool resetManifest) {
            if (resetManifest) {
                _downloader = null;
                _fileManifest = new List < FileManifest > ();
            }
            _expectedFileCount = 0;
            _isUpdating = false; // in case error from download handler during update
            _updateStartTime = 0;
        }


        // deletes all base folders in DataPath that are not CurrentVersion
        private void Cleanup() {
            if (CurrentVersion == null) return;
            String[] dirs = Directory.GetDirectories(DataPath);
            foreach (string dir in dirs) {
                string dirName = dir.Substring(dir.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                if (dirName != CurrentVersion) Directory.Delete(dir, true);
            }
        }

        // starts update procedure of searching for content list
        // and attempting to download
        public bool UpdateProcedure() {
            if (_downloader != null || IsUpdating) return false;
            _fileManifest.Clear();
            _isUpdating = true;
            _updateStartTime = DateTime.Now.Second;
            PlayFabCloudScriptAPI.ExecuteFunction(new ExecuteFunctionRequest()
            {
                Entity = new PlayFab.CloudScriptModels.EntityKey()
                {
                    Id = PlayFabSettings.staticPlayer.EntityId,
                    Type = PlayFabSettings.staticPlayer.EntityType
                },
                FunctionName = _updateScriptName,
                FunctionParameter = null,
                GeneratePlayStreamEvent = false
            }, OnUpdateCloudResult, OnError);
            RecursiveUpdate();
            return true;
        }

        // invoke UpdateProcedure if delay is within '_maxTime'
        // wait 'contentDelayWait' seconds each time
        // otherwise errror
        private async Task RecursiveUpdate() {
            if (!IsUpdating) {
                /* Case update resolved */
                _updateStartTime = 0;
                return;
            }
            await Task.Delay(_delayTime * 1000);
            if (UpdateProcedureB()) {
                    // update resolved do nothing
             } else if ((DateTime.Now.Second - _updateStartTime) < _maxTime) {
                /* Case time is within _maxTime */
                _fileManifest.Clear();
                PlayFabCloudScriptAPI.ExecuteFunction(new ExecuteFunctionRequest()
                {
                Entity = new PlayFab.CloudScriptModels.EntityKey()
                {
                    Id = PlayFabSettings.staticPlayer.EntityId,
                    Type = PlayFabSettings.staticPlayer.EntityType
                },
                FunctionName = _updateScriptName,
                FunctionParameter = null,
                GeneratePlayStreamEvent = false
                }, OnUpdateCloudResult, OnError);
                RecursiveUpdate();
            } else if (IsUpdating) {
                /* Case time expired and update incomplete */
                OnError();
                return;
            } else {
                /* Case update resolved */
                _updateStartTime = 0;
            }
        }

        // populates 'FileManifest'
        // requests URI for non-existant files
        private void OnUpdateCloudResult(ExecuteFunctionResult result) {
            if (result.FunctionResult == null) {
                return;
            }
            var json = JSON.Parse(result.FunctionResult.ToString());
            if (json == null || json["content"] == null) {
                return;
            }
            _expectedFileCount = 0;
            if (json["currentVersion"] != null) _currentVersion = json["currentVersion"]; 

            // build FileManifest
            for (var i = 0; i < json["content"].Count; i++) {
                _fileManifest.Add(new FileManifest {
                    Filename = json["content"][i]["filename"], Name = json["content"][i]["name"], PartialPath = json["content"][i]["contentKey"], Path = DataPath + Path.DirectorySeparatorChar + json["content"][i]["contentKey"]
                });
            }
            // submit request for non-existant files
            for (var i = 0; i < json["content"].Count; i++) {
                if (!File.Exists(FileManifest[i].Path)) {
                    _expectedFileCount += 1;
                    PlayFabClientAPI.GetContentDownloadUrl(new GetContentDownloadUrlRequest { HttpMethod = "GET", Key = json["content"][i]["contentKey"], ThruCDN = true}, OnUpdateCDNResult, OnError);
                }
            }
        }

        // finish populating FileManifest
        private void OnUpdateCDNResult(GetContentDownloadUrlResult result) {
            string partialPath = Utils.FilenameFromURI(result.URL);
            foreach(var manifest in _fileManifest) {
                if (manifest.PartialPath == partialPath) {
                    manifest.URI = result.URL;
                    return;
                }
            }
        }

        // error handling while updating or perhaps downloading
        private void OnError(PlayFabError error = null) {
            if (IsUpdating) {
                Reset(true);
            } // do nothing if errors occur after this point
        }
        
        // checks if a patch is needed and downloads missing files from manifest
        // parses 'HeroData' and 'StructureData'
        // returns true for update resolution, false if error
        private bool UpdateProcedureB() {
            if (_downloader != null || !IsUpdating || _fileManifest == null) return false;
            if (_fileManifest.Count == 0) return false;

            /* 
                Case Manifest incomplete
                A non-existant file did not get assigned a URI
            */
            foreach (var manifest in _fileManifest) {
                if (!File.Exists(manifest.Path) && manifest.URI == null) return false;
            }

            // add the non-existing files to the downloader
            bool missingFiles = false;
            foreach(var manifest in _fileManifest) {
                if (!File.Exists(manifest.Path)) {
                    missingFiles = true;
                    if (_downloader == null) _downloader = new GroupDownloader();
                    _downloader.PendingURLS.Add(manifest.URI);
                    _downloader.URIFilenameMap[manifest.URI] = manifest.PartialPath;
                }
            }

            if (missingFiles) {
                /* Case needs a patch */
                _downloader.Timeout = Timeout;
                _downloader.AbandonOnFailure = true;
                _downloader.OnDownloadFailure += OnUpdateFailure;
                _downloader.OnDownloadSuccess += OnUpdateSuccess;
                _downloader.Download();
            } else {
                /* Case all files up-to-date */
                ProcessManifest();
                Cleanup();
                Reset(false);
            }
            return true;
        }

        // invoked by download handler
        private void OnUpdateFailure(bool completed, string uri, string fileResultPath) {
            /* Case finished */
            if (!_downloader.Downloading || _downloader.DidFinish) {
                Reset(true);
            }
        }

        // invoked by download handler
        private void OnUpdateSuccess(bool completed, string uri, string fileResultPath) {
            /* Case finished */
            if (!_downloader.Downloading || _downloader.DidFinish) {
                ProcessManifest();
                Cleanup();
                Reset(false);
            }
        }

        /*
            Generate AssetType from file type, null if not recognized
        */
        public void ProcessManifest() {
            foreach (var manifest in FileManifest) {
                string filetype = Path.GetExtension(manifest.Path);
                if (filetype == "" || filetype == null) continue;
                if (filetype == ".json") manifest.AssetType = AssetType.JSON;
                if (filetype == ".jpg") manifest.AssetType = AssetType.SPRITE;
                if (filetype == ".png") manifest.AssetType = AssetType.SPRITE;
                if (filetype == ".txt") manifest.AssetType = AssetType.TEXT;
            }
            

            foreach (var manifest in FileManifest) {
                if (manifest.AssetType == null) continue;
                if (manifest.AssetType == AssetType.JSON) {
                    /*`
                        Proccess JSON as a JSONNode
                    */ 
                } else if (manifest.AssetType == AssetType.SPRITE) {
                    /*
                        Proccess image as a Sprite
                    */
  
                } else if (manifest.AssetType == AssetType.TEXT) {
                    /*
                        Proccess as a string
                    */
                }
            }
        }
    }
