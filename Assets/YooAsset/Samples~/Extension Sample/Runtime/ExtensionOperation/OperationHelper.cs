using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using YooAsset;

public class OperationHelper
{
    /// <summary>
    /// 开始一个业务实现的自定义异步任务
    /// </summary>
    public static void StartOperation(AsyncOperationBase operation)
    {
        OperationSystem.StartOperation(OperationSystem.GLOBAL_SCHEDULER_NAME, operation);
    }
}