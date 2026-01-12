using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace YooAsset
{
    internal class OperationSystem
    {
#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnRuntimeInitialize()
        {
            DestroyAll();
        }
#endif

        // 全局调度器名称
        public const string GLOBAL_SCHEDULER_NAME = "YOOASSET_GLOBAL_SCHEDULER";

        private static readonly Dictionary<string, OperationScheduler> _schedulerDic = new Dictionary<string, OperationScheduler>(100);
        private static readonly List<OperationScheduler> _schedulerList = new List<OperationScheduler>(100);
        private static bool _isInitialize = false;
        private static bool _schedulerListDirty = false;
        private static int _createIndex = 0;

        // 计时器相关
        private static Stopwatch _watch;
        private static long _frameTime;

        /// <summary>
        /// 异步操作系统的每帧最大执行预算（毫秒）
        /// </summary>
        public static long MaxTimeSlice { set; get; } = long.MaxValue;

        /// <summary>
        /// 异步操作系统是否繁忙
        /// </summary>
        public static bool IsBusy
        {
            get
            {
                if (_watch == null)
                    return false;

                if (MaxTimeSlice == long.MaxValue)
                    return false;

                // 注意 : 单次调用开销约1微秒
                return _watch.ElapsedMilliseconds - _frameTime >= MaxTimeSlice;
            }
        }


        /// <summary>
        /// 初始化异步操作系统
        /// </summary>
        public static void Initialize()
        {
            _isInitialize = true;
            _watch = Stopwatch.StartNew();

            // 创建全局调度器
            CreatePackageScheduler(GLOBAL_SCHEDULER_NAME, 0);
        }

        /// <summary>
        /// 更新异步操作系统
        /// </summary>
        public static void Update()
        {
            if (_isInitialize == false)
                return;

            // 重新排序调度器
            if (_schedulerListDirty)
            {
                _schedulerListDirty = false;
                _schedulerList.Sort();
            }

            // 更新帧时间
            _frameTime = _watch.ElapsedMilliseconds;

            // 更新调度器
            for (int i = 0; i < _schedulerList.Count; i++)
            {
                if (IsBusy)
                    break;

                _schedulerList[i].Update();
            }
        }

        /// <summary>
        /// 销毁异步操作系统
        /// </summary>
        public static void DestroyAll()
        {
            // 清空所有调度器
            foreach (var scheduler in _schedulerList)
            {
                scheduler.ClearAll();
            }
            _schedulerDic.Clear();
            _schedulerList.Clear();
            _schedulerListDirty = false;
            _createIndex = 0;
            _isInitialize = false;

            _watch = null;
            _frameTime = 0;
            MaxTimeSlice = long.MaxValue;
        }

        /// <summary>
        /// 创建包裹调度器
        /// </summary>
        public static void CreatePackageScheduler(string packageName, int priority)
        {
            if (_schedulerDic.ContainsKey(packageName))
            {
                throw new YooInternalException($"Package scheduler already exists: {packageName}");
            }

            var scheduler = new OperationScheduler(packageName, priority, _createIndex++);
            _schedulerDic.Add(packageName, scheduler);
            _schedulerList.Add(scheduler);
            _schedulerListDirty = true;
        }

        /// <summary>
        /// 销毁包裹调度器
        /// </summary>
        public static void DestroyPackageScheduler(string packageName)
        {
            // 不允许销毁默认调度器
            if (packageName == GLOBAL_SCHEDULER_NAME)
            {
                throw new YooInternalException("Cannot destroy the global package scheduler!");
            }

            if (_schedulerDic.TryGetValue(packageName, out var scheduler))
            {
                scheduler.ClearAll();
                _schedulerDic.Remove(packageName);
                _schedulerList.Remove(scheduler);
            }
        }

        /// <summary>
        /// 销毁包裹的所有任务
        /// </summary>
        public static void ClearPackageOperation(string packageName)
        {
            var scheduler = GetScheduler(packageName);
            scheduler.ClearAll();
        }

        /// <summary>
        /// 开始处理异步操作类
        /// </summary>
        public static void StartOperation(string packageName, AsyncOperationBase operation)
        {
            var scheduler = GetScheduler(packageName);
            scheduler.StartOperation(operation);
        }

        /// <summary>
        /// 获取调度器（严格模式）
        /// </summary>
        private static OperationScheduler GetScheduler(string packageName)
        {
            if (_schedulerDic.TryGetValue(packageName, out var scheduler))
            {
                return scheduler;
            }

            // 严格模式：非默认包裹必须先创建调度器
            throw new YooInternalException($"Package scheduler not found: {packageName}. Please call YooAssets.CreatePackage() first!");
        }

        #region 调试信息
        internal static List<DebugOperationInfo> GetDebugOperationInfos(string packageName)
        {
            var scheduler = GetScheduler(packageName);
            return scheduler.GetDebugOperationInfos();
        }
        #endregion
    }
}
