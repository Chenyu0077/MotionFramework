﻿//--------------------------------------------------
// Motion Framework
// Copyright©2020-2021 何冠峰
// Licensed under the MIT license
//--------------------------------------------------

namespace MotionFramework.Resource
{
	public class AssetBundleInfo
	{
		/// <summary>
		/// 资源包名称
		/// </summary>
		public string BundleName { private set; get; }

		/// <summary>
		/// 本地存储的路径
		/// </summary>
		public string LocalPath { private set; get; }

		/// <summary>
		/// 远端下载地址
		/// </summary>
		public string RemoteURL { private set; get; }

		/// <summary>
		/// 远端下载备用地址
		/// </summary>
		public string RemoteFallbackURL { private set; get; }

		/// <summary>
		/// 资源版本
		/// </summary>
		public int Version { private set; get; }

		/// <summary>
		/// 是否为加密文件
		/// </summary>
		public bool IsEncrypted { private set; get; }

		/// <summary>
		/// 是否为原生文件
		/// </summary>
		public bool IsRawFile { private set; get; }

		public AssetBundleInfo(string bundleName, string localPath, string remoteURL, string remoteFallbackURL, int version, bool isEncrypted, bool isRawFile)
		{
			BundleName = bundleName;
			LocalPath = localPath;
			RemoteURL = remoteURL;
			RemoteFallbackURL = remoteFallbackURL;
			Version = version;
			IsEncrypted = isEncrypted;
			IsRawFile = isRawFile;
		}
		public AssetBundleInfo(string bundleName, string localPath, int version, bool isEncrypted, bool isRawFile)
		{
			BundleName = bundleName;
			LocalPath = localPath;
			RemoteURL = string.Empty;
			RemoteFallbackURL = string.Empty;
			Version = version;
			IsEncrypted = isEncrypted;
			IsRawFile = isRawFile;
		}
		public AssetBundleInfo(string bundleName, string localPath)
		{
			BundleName = bundleName;
			LocalPath = localPath;
			RemoteURL = string.Empty;
			RemoteFallbackURL = string.Empty;
			Version = 0;
			IsEncrypted = false;
			IsRawFile = false;
		}
	}
}