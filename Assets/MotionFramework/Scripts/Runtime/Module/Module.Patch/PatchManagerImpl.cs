﻿//--------------------------------------------------
// Motion Framework
// Copyright©2019-2021 何冠峰
// Copyright©2020-2020 ZensYue
// Licensed under the MIT license
//--------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MotionFramework.AI;
using MotionFramework.Resource;
using MotionFramework.Network;
using MotionFramework.Utility;

namespace MotionFramework.Patch
{
	internal class PatchManagerImpl
	{
		// 流程状态机
		private readonly ProcedureFsm _procedure = new ProcedureFsm();

		// 参数相关
		public bool IgnoreResourceVersion { private set; get; }
		private bool _clearCacheWhenDirty;
		private RemoteServerInfo _serverInfo;
		private string _webPostContent;
		private IGameVersionParser _gameVersionParser;
		private EVerifyLevel _verifyLevel;
		private string[] _autoDownloadDLC;
		private bool _autoDownloadBuildinDLC;
		private int _gameVersionRequestTimeout;
		private int _patchManifestRequestTimeout;
		private int _maxNumberOnLoad;
		private int _failedTryAgain;

		// 补丁清单
		private PatchManifest _appPatchManifest;
		private PatchManifest _localPatchManifest;
		private PatchCache _cache;

		// 补丁下载器
		public PatchDownloader InternalDownloader { private set; get; }

		// 解析内容
		public Version RequestedGameVersion
		{
			get { return _gameVersionParser.GameVersion; }
		}
		public int RequestedResourceVersion
		{
			get { return _gameVersionParser.ResourceVersion; }
		}
		public bool FoundNewApp
		{
			get { return _gameVersionParser.FoundNewApp; }
		}
		public bool ForceInstall
		{
			get { return _gameVersionParser.ForceInstall; }
		}
		public string AppURL
		{
			get { return _gameVersionParser.AppURL; }
		}

		/// <summary>
		/// 当前运行的状态
		/// </summary>
		public string CurrentStates
		{
			get
			{
				return _procedure.Current;
			}
		}

		/// <summary>
		/// 本地的资源版本号
		/// </summary>
		public int LocalResourceVersion
		{
			get
			{
				if (_localPatchManifest == null)
					return -1;
				return _localPatchManifest.ResourceVersion;
			}
		}


		public void Create(PatchManager.CreateParameters createParam)
		{
			IgnoreResourceVersion = createParam.IgnoreResourceVersion;
			_clearCacheWhenDirty = createParam.ClearCacheWhenDirty;
			_serverInfo = createParam.ServerInfo;
			_webPostContent = createParam.WebPoseContent;
			_gameVersionParser = createParam.GameVersionParser;
			_verifyLevel = createParam.VerifyLevel;
			_autoDownloadDLC = createParam.AutoDownloadDLC;
			_autoDownloadBuildinDLC = createParam.AutoDownloadBuildinDLC;
			_gameVersionRequestTimeout = createParam.GameVersionRequestTimeout;
			_patchManifestRequestTimeout = createParam.PatchManifestRequestTimeout;
			_maxNumberOnLoad = createParam.MaxNumberOnLoad;
			_failedTryAgain = createParam.FailedTryAgain;
		}

		/// <summary>
		/// 异步初始化
		/// </summary>
		public IEnumerator InitializeAsync()
		{
			MotionLog.Log($"Beginning to initialize patch manager.");

			// 加载缓存
			_cache = PatchCache.LoadCache();

			// 检测沙盒被污染
			{
				// 如果是首次打开，记录APP版本号
				if (PatchHelper.CheckSandboxCacheFileExist() == false)
				{
					_cache.InitCache(Application.version);
				}
				else
				{
					// 每次启动时比对APP版本号是否一致	
					if (_cache.CacheAppVersion != Application.version)
					{
						// 注意：在覆盖安装的时候，会保留沙盒目录里的文件，可以选择清空沙盒目录
						if (_clearCacheWhenDirty)
						{
							MotionLog.Warning($"Cache is dirty ! Cache app version is {_cache.CacheAppVersion}, Current app version is {Application.version}");
							ClearCache();

							// 重新写入最新的APP版本号
							_cache.InitCache(Application.version);
						}
						else
						{
							// 删除清单文件
							PatchHelper.DeleteSandboxPatchManifestFile();

							// 重新写入最新的APP版本号
							_cache.InitCache(Application.version);
						}
					}
				}
			}

			// 加载APP内的补丁清单
			MotionLog.Log($"Load app patch manifest.");
			{
				string filePath = AssetPathHelper.MakeStreamingLoadPath(PatchDefine.PatchManifestFileName);
				string url = AssetPathHelper.ConvertToWWWPath(filePath);
				WebGetRequest downloader = new WebGetRequest(url);
				downloader.SendRequest();
				yield return downloader;

				if (downloader.HasError())
				{
					downloader.ReportError();
					downloader.Dispose();
					throw new System.Exception($"Fatal error : Failed download file : {url}");
				}

				// 解析补丁清单
				string jsonData = downloader.GetText();
				_appPatchManifest = PatchManifest.Deserialize(jsonData);
				downloader.Dispose();
			}

			// 加载沙盒内的补丁清单			
			if (PatchHelper.CheckSandboxPatchManifestFileExist())
			{
				MotionLog.Log($"Load sandbox patch manifest.");
				string filePath = AssetPathHelper.MakePersistentLoadPath(PatchDefine.PatchManifestFileName);
				string jsonData = File.ReadAllText(filePath);
				_localPatchManifest = PatchManifest.Deserialize(jsonData);
			}
			else
			{
				_localPatchManifest = _appPatchManifest;
			}
		}

		/// <summary>
		/// 开启更新
		/// </summary>
		public void Download()
		{
			MotionLog.Log("Begin to run patch procedure.");

			// 注意：按照先后顺序添加流程节点
			_procedure.AddNode(new FsmRequestGameVersion(this));
			_procedure.AddNode(new FsmRequestPatchManifest(this));
			_procedure.AddNode(new FsmGetDownloadList(this));
			_procedure.AddNode(new FsmDownloadWebFiles(this));
			_procedure.AddNode(new FsmDownloadOver(this));
			_procedure.AddNode(new FsmPatchDone());
			_procedure.Run();
		}

		/// <summary>
		/// 更新流程
		/// </summary>
		public void Update()
		{
			_procedure.Update();
		}

		/// <summary>
		/// 清空缓存并删除所有沙盒文件
		/// </summary>
		public void ClearCache()
		{
			MotionLog.Warning("Clear cache and remove all sandbox files.");
			PatchHelper.ClearSandbox();
		}

		/// <summary>
		/// 处理请求操作
		/// </summary>
		public void HandleOperation(EPatchOperation operation)
		{
			if (operation == EPatchOperation.BeginGetDownloadList)
			{
				// 从挂起的地方继续
				if (_procedure.Current == EPatchStates.RequestPatchManifest.ToString())
					_procedure.SwitchNext();
				else
					MotionLog.Error($"Patch states is incorrect : {_procedure.Current}");
			}
			else if (operation == EPatchOperation.BeginDownloadWebFiles)
			{
				// 从挂起的地方继续
				if (_procedure.Current == EPatchStates.GetDownloadList.ToString())
					_procedure.SwitchNext();
				else
					MotionLog.Error($"Patch states is incorrect : {_procedure.Current}");
			}
			else if (operation == EPatchOperation.TryRequestGameVersion)
			{
				// 修复当前错误节点
				if (_procedure.Current == EPatchStates.RequestGameVersion.ToString())
					_procedure.Switch(_procedure.Current);
				else
					MotionLog.Error($"Patch states is incorrect : {_procedure.Current}");
			}
			else if (operation == EPatchOperation.TryRequestPatchManifest)
			{
				// 修复当前错误节点
				if (_procedure.Current == EPatchStates.RequestPatchManifest.ToString())
					_procedure.Switch(_procedure.Current);
				else
					MotionLog.Error($"Patch states is incorrect : {_procedure.Current}");
			}
			else if (operation == EPatchOperation.TryDownloadWebFiles)
			{
				// 修复当前错误节点
				if (_procedure.Current == EPatchStates.DownloadWebFiles.ToString())
					_procedure.Switch(EPatchStates.GetDownloadList.ToString());
				else
					MotionLog.Error($"Patch states is incorrect : {_procedure.Current}");
			}
			else
			{
				throw new NotImplementedException($"{operation}");
			}
		}

		/// <summary>
		/// 获取AssetBundle的加载信息
		/// </summary>
		public AssetBundleInfo GetAssetBundleInfo(string bundleName)
		{
			if (_localPatchManifest.Bundles.TryGetValue(bundleName, out PatchBundle patchBundle))
			{
				// 查询APP资源
				if (_appPatchManifest.Bundles.TryGetValue(bundleName, out PatchBundle appPatchBundle))
				{
					if (appPatchBundle.IsBuildin && appPatchBundle.Hash == patchBundle.Hash)
					{
						string appLoadPath = AssetPathHelper.MakeStreamingLoadPath(appPatchBundle.Hash);
						AssetBundleInfo bundleInfo = new AssetBundleInfo(bundleName, appLoadPath, appPatchBundle.Version, appPatchBundle.IsEncrypted);
						return bundleInfo;
					}
				}

				// 查询缓存资源
				// 注意：如果沙盒内缓存文件不存在，那么将会从服务器下载
				string sandboxLoadPath = PatchHelper.MakeSandboxCacheFilePath(patchBundle.Hash);
				if (_cache.Contains(patchBundle.Hash))
				{
					AssetBundleInfo bundleInfo = new AssetBundleInfo(bundleName, sandboxLoadPath, patchBundle.Version, patchBundle.IsEncrypted);
					return bundleInfo;
				}
				else
				{
					string remoteURL = GetPatchDownloadURL(patchBundle.Version, patchBundle.Hash);
					string remoteFallbackURL = GetPatchDownloadFallbackURL(patchBundle.Version, patchBundle.Hash);
					AssetBundleInfo bundleInfo = new AssetBundleInfo(bundleName, sandboxLoadPath, remoteURL, remoteFallbackURL, patchBundle.Version, patchBundle.IsEncrypted);
					return bundleInfo;
				}
			}
			else
			{
				MotionLog.Warning($"Not found bundle in patch manifest : {bundleName}");
				AssetBundleInfo bundleInfo = new AssetBundleInfo(bundleName, string.Empty);
				return bundleInfo;
			}
		}

		/// <summary>
		/// 获取更新游戏时的下载列表
		/// </summary>
		public List<PatchBundle> GetAutoPatchDownloadList()
		{
			List<string> dlcTags = new List<string>();
			if (_autoDownloadDLC != null)
				dlcTags.AddRange(_autoDownloadDLC);
			if (_autoDownloadBuildinDLC)
				dlcTags.AddRange(_appPatchManifest.GetBuildinTags());

			return GetPatchDownloadList(dlcTags.ToArray());
		}

		/// <summary>
		/// 获取补丁下载列表
		/// </summary>
		public List<PatchBundle> GetPatchDownloadList(string[] dlcTags)
		{
			List<PatchBundle> downloadList = new List<PatchBundle>(1000);
			foreach (var patchBundle in _localPatchManifest.BundleList)
			{
				// 忽略缓存资源
				if (_cache.Contains(patchBundle.Hash))
					continue;

				// 忽略APP资源
				// 注意：如果是APP资源并且哈希值相同，则不需要下载
				if (_appPatchManifest.Bundles.TryGetValue(patchBundle.BundleName, out PatchBundle appPatchBundle))
				{
					if (appPatchBundle.IsBuildin && appPatchBundle.Hash == patchBundle.Hash)
						continue;
				}

				// 如果是纯内置资源，则统一下载
				// 注意：可能是新增的或者变化的内置资源
				// 注意：可能是由热更资源转换的内置资源
				if (patchBundle.IsPureBuildin())
				{
					downloadList.Add(patchBundle);
				}
				else
				{
					// 查询DLC资源
					if (patchBundle.HasTag(dlcTags))
					{
						downloadList.Add(patchBundle);
					}
				}
			}

			return CacheAndFilterDownloadList(downloadList);
		}

		/// <summary>
		/// 创建内置的加载器
		/// </summary>
		public void CreateInternalDownloader(List<PatchBundle> downloadList)
		{
			MotionLog.Log("Create internal patch downloader.");
			InternalDownloader = new PatchDownloader(this, downloadList, _maxNumberOnLoad, _failedTryAgain);
		}

		// 检测下载内容的完整性并缓存
		public bool CheckContentIntegrity(string bundleName)
		{
			if (_localPatchManifest.Bundles.TryGetValue(bundleName, out PatchBundle patchBundle))
			{
				return CheckContentIntegrity(patchBundle);
			}
			else
			{
				MotionLog.Warning($"Not found check content file in local patch manifest : {bundleName}");
				return false;
			}
		}
		public bool CheckContentIntegrity(PatchBundle patchBundle)
		{
			return CheckContentIntegrity(patchBundle.Hash, patchBundle.CRC, patchBundle.SizeBytes);
		}
		private bool CheckContentIntegrity(string hash, string crc, long size)
		{
			string filePath = PatchHelper.MakeSandboxCacheFilePath(hash);
			if (File.Exists(filePath) == false)
				return false;

			// 校验沙盒里的补丁文件
			if (_verifyLevel == EVerifyLevel.Size)
			{
				long fileSize = FileUtility.GetFileSize(filePath);
				return fileSize == size;
			}
			else if (_verifyLevel == EVerifyLevel.CRC)
			{
				string fileCRC = HashUtility.FileCRC32(filePath);
				return fileCRC == crc;
			}
			else
			{
				throw new NotImplementedException(_verifyLevel.ToString());
			}
		}

		// 缓存系统相关
		public void CacheDownloadPatchFile(string bundleName)
		{
			if (_localPatchManifest.Bundles.TryGetValue(bundleName, out PatchBundle patchBundle))
			{
				MotionLog.Log($"Cache download web file : {patchBundle.BundleName} Version : {patchBundle.Version} Hash : {patchBundle.Hash}");
				_cache.CacheDownloadPatchFile(patchBundle.Hash);
			}
			else
			{
				MotionLog.Warning($"Not found bundle in local patch manifest : {bundleName}");
			}
		}
		public void CacheDownloadPatchFiles(List<PatchBundle> downloadList)
		{
			List<string> hashList = new List<string>(downloadList.Count);
			foreach (var patchBundle in downloadList)
			{
				MotionLog.Log($"Cache download web file : {patchBundle.BundleName} Version : {patchBundle.Version} Hash : {patchBundle.Hash}");
				hashList.Add(patchBundle.Hash);
			}
			_cache.CacheDownloadPatchFiles(hashList);
		}
		private List<PatchBundle> CacheAndFilterDownloadList(List<PatchBundle> downloadList)
		{
			// 检测文件是否已经下载完毕
			// 注意：如果玩家在加载过程中强制退出，下次再进入的时候跳过已经加载的文件
			List<PatchBundle> cacheList = new List<PatchBundle>();
			for (int i = downloadList.Count - 1; i >= 0; i--)
			{
				var patchBundle = downloadList[i];
				if (CheckContentIntegrity(patchBundle))
				{
					cacheList.Add(patchBundle);
					downloadList.RemoveAt(i);
				}
			}

			// 缓存已经下载的有效文件
			if (cacheList.Count > 0)
				CacheDownloadPatchFiles(cacheList);

			return downloadList;
		}

		// 补丁清单相关
		public PatchManifest GetPatchManifest()
		{
			return _localPatchManifest;
		}
		public void ParseRemotePatchManifest(string content)
		{
			_localPatchManifest = PatchManifest.Deserialize(content);
		}
		public void SaveRemotePatchManifest()
		{
			// 注意：这里会覆盖掉沙盒内的补丁清单文件
			MotionLog.Log("Save remote patch manifest.");
			string savePath = AssetPathHelper.MakePersistentLoadPath(PatchDefine.PatchManifestFileName);
			PatchManifest.Serialize(savePath, _localPatchManifest);
		}

		// 流程相关
		public void Switch(EPatchStates patchStates)
		{
			_procedure.Switch(patchStates.ToString());
		}
		public void SwitchNext()
		{
			_procedure.SwitchNext();
		}
		public void SwitchLast()
		{
			_procedure.SwitchLast();
		}

		// WEB相关
		public int GetGameVersionRequestTimeout()
		{
			return _gameVersionRequestTimeout;
		}
		public int GetPatchManifestRequestTimeout()
		{
			return _patchManifestRequestTimeout;
		}
		public string GetPatchDownloadURL(int resourceVersion, string fileName)
		{
			RuntimePlatform runtimePlatform = Application.platform;
			string cdnServer = _serverInfo.GetCDNServer(runtimePlatform);
			return $"{cdnServer}/{resourceVersion}/{fileName}";
		}
		public string GetPatchDownloadFallbackURL(int resourceVersion, string fileName)
		{
			RuntimePlatform runtimePlatform = Application.platform;
			string cdnFallbackServer = _serverInfo.GetCDNFallbackServer(runtimePlatform);
			return $"{cdnFallbackServer}/{resourceVersion}/{fileName}";
		}
		public string GetWebServerURL()
		{
			RuntimePlatform runtimePlatform = Application.platform;
			return _serverInfo.GetWebServer(runtimePlatform);
		}
		public string GetWebPostContent()
		{
			return _webPostContent;
		}
		public bool ParseResponseContent(string content)
		{
			return _gameVersionParser.ParseContent(content);
		}
	}
}