﻿//--------------------------------------------------
// Motion Framework
// Copyright©2019-2020 何冠峰
// Licensed under the MIT license
//--------------------------------------------------
using System.IO;
using System.Text;
using MotionFramework.Resource;

namespace MotionFramework.Patch
{
	internal static class PatchHelper
	{
		private const string StrCacheFileName = "Cache.bytes";
		private const string StrCacheFolderName = "CacheFiles";
		
		/// <summary>
		/// 清空沙盒目录
		/// </summary>
		public static void ClearSandbox()
		{
			string directoryPath = AssetPathHelper.MakePersistentLoadPath(string.Empty);
			if(Directory.Exists(directoryPath))
				Directory.Delete(directoryPath, true);
		}

		/// <summary>
		/// 删除沙盒内补丁清单文件
		/// </summary>
		public static void DeleteSandboxPatchManifestFile()
		{
			string filePath = AssetPathHelper.MakePersistentLoadPath(PatchDefine.PatchManifestFileName);
			if (File.Exists(filePath))
				File.Delete(filePath);
		}

		/// <summary>
		/// 删除沙盒内的缓存文件夹
		/// </summary>
		public static void DeleteSandboxCacheFolder()
		{
			string directoryPath = AssetPathHelper.MakePersistentLoadPath(StrCacheFolderName);
			if (Directory.Exists(directoryPath))
				Directory.Delete(directoryPath, true);
		}


		/// <summary>
		/// 获取沙盒内缓存文件的路径
		/// </summary>
		public static string GetSandboxCacheFilePath()
		{
			return AssetPathHelper.MakePersistentLoadPath(StrCacheFileName);
		}

		/// <summary>
		/// 检测沙盒内缓存文件是否存在
		/// </summary>
		public static bool CheckSandboxCacheFileExist()
		{
			string filePath = GetSandboxCacheFilePath();
			return File.Exists(filePath);
		}

		/// <summary>
		/// 检测沙盒内补丁清单文件是否存在
		/// </summary>
		public static bool CheckSandboxPatchManifestFileExist()
		{
			string filePath = AssetPathHelper.MakePersistentLoadPath(PatchDefine.PatchManifestFileName);
			return File.Exists(filePath);
		}

		/// <summary>
		/// 获取缓存文件的存储路径
		/// </summary>
		public static string MakeSandboxCacheFilePath(string fileName)
		{
			return AssetPathHelper.MakePersistentLoadPath($"{StrCacheFolderName}/{fileName}");
		}
	}
}