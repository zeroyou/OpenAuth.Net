using System;
using System.Collections.Generic;
using System.Linq;
using Infrastructure;
using OpenAuth.App.Flow;
using OpenAuth.App.Request;
using OpenAuth.App.Response;
using OpenAuth.App.SSO;
using OpenAuth.Repository.Domain;

namespace OpenAuth.App
{
    /// <summary>
    /// 工作流实例表操作
    /// </summary>
    public class FlowInstanceApp :BaseApp<FlowInstance>
    {
        #region 提交数据

        /// <summary>
        /// 存储工作流实例进程（审核驳回重新提交）
        /// </summary>
        /// <param name="processInstanceEntity"></param>
        /// <param name="processSchemeEntity"></param>
        /// <param name="processOperationHistoryEntity"></param>
        /// <param name="processTransitionHistoryEntity"></param>
        /// <returns></returns>
        public int SaveProcess(FlowInstance processInstanceEntity,  
            FlowInstanceOperationHistory processOperationHistoryEntity,  FlowInstanceTransitionHistory processTransitionHistoryEntity = null)
        {
            try
            {
                processInstanceEntity.Id=(processInstanceEntity.Id);
                UnitWork.Update(processInstanceEntity);

                processOperationHistoryEntity.InstanceId = processInstanceEntity.Id;
                UnitWork.Add(processOperationHistoryEntity);

                if (processTransitionHistoryEntity != null)
                {
                    processTransitionHistoryEntity.InstanceId = processInstanceEntity.Id;
                    UnitWork.Add(processTransitionHistoryEntity);
                }
               
                UnitWork.Save();
                return 1;
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        ///  更新流程实例 审核节点用
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="dbbaseId"></param>
        /// <param name="processInstanceEntity"></param>
        /// <param name="processSchemeEntity"></param>
        /// <param name="processOperationHistoryEntity"></param>
        /// <param name="delegateRecordEntityList"></param>
        /// <param name="processTransitionHistoryEntity"></param>
        /// <returns></returns>
        public int SaveProcess(string sql,string dbbaseId, FlowInstance processInstanceEntity,  FlowInstanceOperationHistory processOperationHistoryEntity, FlowInstanceTransitionHistory processTransitionHistoryEntity = null)
        {
            try
            {
                processInstanceEntity.Id=(processInstanceEntity.Id);
                UnitWork.Update(processInstanceEntity);

                processOperationHistoryEntity.InstanceId = processInstanceEntity.Id;
                UnitWork.Add(processOperationHistoryEntity);

                if (processTransitionHistoryEntity != null)
                {
                    processTransitionHistoryEntity.InstanceId = processInstanceEntity.Id;
                    UnitWork.Add(processTransitionHistoryEntity);
                }
               
                //if (!string.IsNullOrEmpty(dbbaseId) && !string.IsNullOrEmpty(sql))//测试环境不允许执行sql语句
                //{
                //    DataBaseLinkEntity dataBaseLinkEntity = dataBaseLinkService.GetEntity(dbbaseId);//获取
                //    this.BaseRepository(dataBaseLinkEntity.DbConnection).ExecuteBySql(sql.Replace("{0}", processInstanceEntity.Id));
                //}
                UnitWork.Save();
                return 1;
            }
            catch
            {
                throw;
            }
        }

        #endregion

        
        #region 流程处理API
        /// <summary>
        /// 创建一个实例
        /// </summary>
        /// <param name="processId">进程GUID</param>
        /// <param name="schemeInfoId">模板信息ID</param>
        /// <param name="wfLevel"></param>
        /// <param name="code">进程编号</param>
        /// <param name="customName">自定义名称</param>
        /// <param name="description">备注</param>
        /// <param name="frmData">表单数据信息</param>
        /// <returns></returns>
        public bool CreateInstance(FlowInstance flowInstance)
        {
            var wfruntime = new FlowRuntime(flowInstance);


            var user = AuthUtil.GetCurrentUser();
            #region 实例信息
            flowInstance.ActivityId = wfruntime.runtimeModel.nextNodeId;
            flowInstance.ActivityType = wfruntime.GetNextNodeType();//-1无法运行,0会签开始,1会签结束,2一般节点,4流程运行结束
            flowInstance.ActivityName = wfruntime.runtimeModel.nextNode.name;
            flowInstance.PreviousId = wfruntime.runtimeModel.currentNodeId;
            flowInstance.CreateUserId = user.User.Id;
            flowInstance.CreateUserName = user.User.Account;
            flowInstance.MakerList = (wfruntime.GetNextNodeType() != 4 ? GetMakerList(wfruntime) : "");//当前节点可执行的人信息
            flowInstance.IsFinish = (wfruntime.GetNextNodeType() == 4 ? 1 : 0);
            #endregion

            #region 流程操作记录
            FlowInstanceOperationHistory processOperationHistoryEntity = new FlowInstanceOperationHistory
            {
                InstanceId = flowInstance.Id,
                Content = "【创建】"
                          + user.User.Name
                          + "创建了一个流程进程【"
                          + flowInstance.Code + "/"
                          + flowInstance.CustomName + "】"
            };

            #endregion

            #region 流转记录

            FlowInstanceTransitionHistory processTransitionHistoryEntity = new FlowInstanceTransitionHistory
            {
                InstanceId = flowInstance.Id,
                FromNodeId = wfruntime.runtimeModel.currentNodeId,
                FromNodeName = wfruntime.runtimeModel.currentNode.name.Value,
                FromNodeType = wfruntime.runtimeModel.currentNodeType,
                ToNodeId = wfruntime.runtimeModel.nextNodeId,
                ToNodeName = wfruntime.runtimeModel.nextNode.name,
                ToNodeType = wfruntime.runtimeModel.nextNodeType,
                TransitionSate = 0
            };
            processTransitionHistoryEntity.IsFinish = (processTransitionHistoryEntity.ToNodeType == 4 ? 1 : 0);
            #endregion
            
            UnitWork.Add(flowInstance);
            UnitWork.Add(processOperationHistoryEntity);
            UnitWork.Add(processTransitionHistoryEntity);
            UnitWork.Save();
            return true;
        }

        /// <summary>
        /// 节点审核
        /// </summary>
        /// <param name="processId"></param>
        /// <returns></returns>
        public bool NodeVerification(string processId, bool flag, string description = "")
        {
            bool _res = false;
            try
            {
                string _sqlstr = "", _dbbaseId = "";
                FlowInstance FlowInstance = Get(processId);
                FlowInstanceOperationHistory FlowInstanceOperationHistory = new FlowInstanceOperationHistory();//操作记录
                FlowInstanceTransitionHistory processTransitionHistoryEntity = null;//流转记录

                FlowRuntime wfruntime = new FlowRuntime(FlowInstance);


                #region 会签
                if (FlowInstance.ActivityType == 0)//会签
                {
                    wfruntime.MakeTagNode(wfruntime.runtimeModel.currentNodeId, 1, "");//标记当前节点通过
                    ///寻找需要审核的节点Id
                    string _VerificationNodeId = "";
                    List<string> _nodelist = wfruntime.GetCountersigningNodeIdList(wfruntime.runtimeModel.currentNodeId);
                    string _makerList = "";
                    foreach (string item in _nodelist)
                    {
                        _makerList = GetMakerList(wfruntime.runtimeModel.nodes[item], wfruntime.runtimeModel.flowInstanceId);
                        if (_makerList != "-1")
                        {
                            var id = AuthUtil.GetCurrentUser().User.Id;
                            foreach (string one in _makerList.Split(','))
                            {
                                if (id == one || id.IndexOf(one) != -1)
                                {
                                    _VerificationNodeId = item;
                                    break;
                                }
                            }
                        }
                    }

                    if (_VerificationNodeId != "")
                    {
                        if (flag)
                        {
                            FlowInstanceOperationHistory.Content = "【" + "todo name" + "】【" + wfruntime.runtimeModel.nodes[_VerificationNodeId].name + "】【" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "】同意,备注：" + description;
                        }
                        else
                        {
                            FlowInstanceOperationHistory.Content = "【" + "todo name" + "】【" + wfruntime.runtimeModel.nodes[_VerificationNodeId].name + "】【" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "】不同意,备注：" + description;
                        }

                        string _Confluenceres = wfruntime.NodeConfluence(_VerificationNodeId, flag, AuthUtil.GetCurrentUser().User.Id, description);
                        var _data = new
                        {
                            SchemeContent = wfruntime.runtimeModel.schemeContentJson.ToString(), wfruntime.runtimeModel.frmData
                        };
                        switch (_Confluenceres)
                        {
                            case "-1"://不通过
                                FlowInstance.IsFinish = 3;
                                break;
                            case "1"://等待
                                break;
                            default://通过
                                FlowInstance.PreviousId = FlowInstance.ActivityId;
                                FlowInstance.ActivityId = wfruntime.runtimeModel.nextNodeId;
                                FlowInstance.ActivityType = wfruntime.runtimeModel.nextNodeType;//-1无法运行,0会签开始,1会签结束,2一般节点,4流程运行结束
                                FlowInstance.ActivityName = wfruntime.runtimeModel.nextNode.name;
                                FlowInstance.IsFinish = (wfruntime.runtimeModel.nextNodeType == 4 ? 1 : 0);
                                FlowInstance.MakerList = (wfruntime.runtimeModel.nextNodeType == 4 ?"": GetMakerList(wfruntime) );//当前节点可执行的人信息

                                #region 流转记录
                                processTransitionHistoryEntity = new FlowInstanceTransitionHistory();
                                processTransitionHistoryEntity.FromNodeId = wfruntime.runtimeModel.currentNodeId;
                                processTransitionHistoryEntity.FromNodeName = wfruntime.runtimeModel.currentNode.name.Value;
                                processTransitionHistoryEntity.FromNodeType = wfruntime.runtimeModel.currentNodeType;
                                processTransitionHistoryEntity.ToNodeId = wfruntime.runtimeModel.nextNodeId;
                                processTransitionHistoryEntity.ToNodeName = wfruntime.runtimeModel.nextNode.name;
                                processTransitionHistoryEntity.ToNodeType = wfruntime.runtimeModel.nextNodeType;
                                processTransitionHistoryEntity.TransitionSate = 0;
                                processTransitionHistoryEntity.IsFinish = (processTransitionHistoryEntity.ToNodeType == 4 ? 1 : 0);
                                #endregion



                                if (wfruntime.runtimeModel.currentNode.setInfo != null && wfruntime.runtimeModel.currentNode.setInfo.NodeSQL != null)
                                {
                                    _sqlstr = wfruntime.runtimeModel.currentNode.setInfo.NodeSQL.Value;
                                    _dbbaseId = wfruntime.runtimeModel.currentNode.setInfo.NodeDataBaseToSQL.Value;
                                }
                                break;
                        }
                    }
                    else
                    {
                        throw (new Exception("审核异常,找不到审核节点"));
                    }
                }
                #endregion

                #region 一般审核
                else//一般审核
                {
                    if (flag)
                    {
                        wfruntime.MakeTagNode(wfruntime.runtimeModel.currentNodeId, 1, AuthUtil.GetCurrentUser().User.Id, description);
                        FlowInstance.PreviousId = FlowInstance.ActivityId;
                        FlowInstance.ActivityId = wfruntime.runtimeModel.nextNodeId;
                        FlowInstance.ActivityType = wfruntime.runtimeModel.nextNodeType;//-1无法运行,0会签开始,1会签结束,2一般节点,4流程运行结束
                        FlowInstance.ActivityName = wfruntime.runtimeModel.nextNode.name;
                        FlowInstance.MakerList = wfruntime.runtimeModel.nextNodeType == 4 ? "" : GetMakerList(wfruntime);//当前节点可执行的人信息
                        FlowInstance.IsFinish = (wfruntime.runtimeModel.nextNodeType == 4 ? 1 : 0);
                        #region 流转记录

                        processTransitionHistoryEntity = new FlowInstanceTransitionHistory
                        {
                            FromNodeId = wfruntime.runtimeModel.currentNodeId,
                            FromNodeName = wfruntime.runtimeModel.currentNode.name.Value,
                            FromNodeType = wfruntime.runtimeModel.currentNodeType,
                            ToNodeId = wfruntime.runtimeModel.nextNodeId,
                            ToNodeName = wfruntime.runtimeModel.nextNode.name,
                            ToNodeType = wfruntime.runtimeModel.nextNodeType,
                            TransitionSate = 0
                        };
                        processTransitionHistoryEntity.IsFinish = (processTransitionHistoryEntity.ToNodeType == 4 ? 1 : 0);
                        #endregion



                        if (wfruntime.runtimeModel.currentNode.setInfo != null && wfruntime.runtimeModel.currentNode.setInfo.NodeSQL != null)
                        {
                            _sqlstr = wfruntime.runtimeModel.currentNode.setInfo.NodeSQL.Value;
                            _dbbaseId = wfruntime.runtimeModel.currentNode.setInfo.NodeDataBaseToSQL.Value;
                        }

                        FlowInstanceOperationHistory.Content = "【" + "todo name" + "】【" + wfruntime.runtimeModel.currentNode.name + "】【" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "】同意,备注：" + description;
                    }
                    else
                    {
                        FlowInstance.IsFinish = 3; //表示该节点不同意
                        wfruntime.MakeTagNode(wfruntime.runtimeModel.currentNodeId, -1, AuthUtil.GetUserName(), description);

                        FlowInstanceOperationHistory.Content = "【" + "todo name" + "】【" + wfruntime.runtimeModel.currentNode.name + "】【" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "】不同意,备注：" + description;
                    }
                    var data = new
                    {
                        SchemeContent = wfruntime.runtimeModel.schemeContentJson.ToString(), wfruntime.runtimeModel.frmData 
                    };
                }
                #endregion 

                _res = true;
                SaveProcess(_sqlstr, _dbbaseId, FlowInstance, FlowInstanceOperationHistory, processTransitionHistoryEntity);
                return _res;
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// 驳回
        /// </summary>
        /// <param name="processId"></param>
        /// <param name="nodeId"></param>
        /// <param name="description"></param>
        /// <returns></returns>
        public bool NodeReject(string processId, string nodeId, string description = "")
        {
            try
            {
                FlowInstance flowInstance = Get(processId);
                FlowInstanceOperationHistory flowInstanceOperationHistory = new FlowInstanceOperationHistory();
                FlowInstanceTransitionHistory processTransitionHistoryEntity = null;
               
                FlowRuntime wfruntime = new FlowRuntime(flowInstance);


                string resnode = "";
                if (nodeId == "")
                {
                    resnode = wfruntime.RejectNode();
                }
                else
                {
                    resnode = nodeId;
                }
                wfruntime.MakeTagNode(wfruntime.runtimeModel.currentNodeId, 0, AuthUtil.GetUserName(), description);
                flowInstance.IsFinish = 4;//4表示驳回（需要申请者重新提交表单）
                if (resnode != "")
                {
                    flowInstance.PreviousId = flowInstance.ActivityId;
                    flowInstance.ActivityId = resnode;
                    flowInstance.ActivityType = wfruntime.GetNodeType(resnode);//-1无法运行,0会签开始,1会签结束,2一般节点,4流程运行结束
                    flowInstance.ActivityName = wfruntime.runtimeModel.nodes[resnode].name;
                    flowInstance.MakerList = GetMakerList(wfruntime.runtimeModel.nodes[resnode], flowInstance.PreviousId);//当前节点可执行的人信息
                    #region 流转记录
                    processTransitionHistoryEntity = new FlowInstanceTransitionHistory();
                    processTransitionHistoryEntity.FromNodeId = wfruntime.runtimeModel.currentNodeId;
                    processTransitionHistoryEntity.FromNodeName = wfruntime.runtimeModel.currentNode.name.Value;
                    processTransitionHistoryEntity.FromNodeType = wfruntime.runtimeModel.currentNodeType;
                    processTransitionHistoryEntity.ToNodeId = wfruntime.runtimeModel.nextNodeId;
                    processTransitionHistoryEntity.ToNodeName = wfruntime.runtimeModel.nextNode.name;
                    processTransitionHistoryEntity.ToNodeType = wfruntime.runtimeModel.nextNodeType;
                    processTransitionHistoryEntity.TransitionSate = 1;//
                    processTransitionHistoryEntity.IsFinish = (processTransitionHistoryEntity.ToNodeType == 4 ? 1 : 0);
                    #endregion
                }
                var data = new
                {
                    SchemeContent = wfruntime.runtimeModel.schemeContentJson.ToString(),
                    frmData = (flowInstance.FrmType == 0 ? wfruntime.runtimeModel.frmData : null)
                };
                flowInstanceOperationHistory.Content = "【" + "todo name" + "】【" + wfruntime.runtimeModel.currentNode.name + "】【" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "】驳回,备注：" + description;

                SaveProcess(flowInstance, flowInstanceOperationHistory, processTransitionHistoryEntity);
                return true;
            }
            catch
            {
                throw;
            }
        }
        #endregion

        /// <summary>
        /// 寻找该节点执行人
        /// </summary>
        /// <param name="wfruntime"></param>
        /// <returns></returns>
        private string GetMakerList(FlowRuntime wfruntime)
        {
            try
            {
                string makerList = "";
                if (wfruntime.runtimeModel.nextNodeId == "-1")
                {
                    throw (new Exception("无法寻找到下一个节点"));
                }
                if (wfruntime.runtimeModel.nextNodeType == 0)//如果是会签节点
                {
                    List<string> _nodelist = wfruntime.GetCountersigningNodeIdList(wfruntime.runtimeModel.nextNodeId);
                    string _makerList = "";
                    foreach (string item in _nodelist)
                    {
                        _makerList = GetMakerList(wfruntime.runtimeModel.nodes[item], wfruntime.runtimeModel.flowInstanceId);
                        if (_makerList == "-1")
                        {
                            throw (new Exception("无法寻找到会签节点的审核者,请查看流程设计是否有问题!"));
                        }
                        if (_makerList == "1")
                        {
                            throw (new Exception("会签节点的审核者不能为所有人,请查看流程设计是否有问题!"));
                        }
                        if (makerList != "")
                        {
                            makerList += ",";
                        }
                        makerList += _makerList;
                    }
                }
                else
                {
                    makerList = GetMakerList(wfruntime.runtimeModel.nextNode, wfruntime.runtimeModel.flowInstanceId);
                    if (makerList == "-1")
                    {
                        throw (new Exception("无法寻找到节点的审核者,请查看流程设计是否有问题!"));
                    }
                }

                return makerList;
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// 寻找该节点执行人
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private string GetMakerList(dynamic node, string processId)
        {
            try
            {
                string makerlsit = "";

                if (node.setInfo == null)
                {
                    makerlsit = "-1";
                }
                else
                {
                    if (node.setInfo.NodeDesignate.Value == "NodeDesignateType1")//所有成员
                    {
                        makerlsit = "1";
                    }
                    else if (node.setInfo.NodeDesignate.Value == "NodeDesignateType2")//指定成员
                    {
                        makerlsit = ArrwyToString(node.setInfo.NodeDesignateData.role, makerlsit);
                        makerlsit = ArrwyToString(node.setInfo.NodeDesignateData.post, makerlsit);
                        makerlsit = ArrwyToString(node.setInfo.NodeDesignateData.usergroup, makerlsit);
                        makerlsit = ArrwyToString(node.setInfo.NodeDesignateData.user, makerlsit);

                        if (makerlsit == "")
                        {
                            makerlsit = "-1";
                        }
                    }
                    //else if (node.setInfo.NodeDesignate.Value == "NodeDesignateType3")//发起者领导
                    //{
                    //    UserEntity userEntity = userService.GetEntity(OperatorProvider.Provider.Current().UserId);
                    //    if (string.IsNullOrEmpty(userEntity.ManagerId))
                    //    {
                    //        makerlsit = "-1";
                    //    }
                    //    else
                    //    {
                    //        makerlsit = userEntity.ManagerId;
                    //    }
                    //}
                    //else if (node.setInfo.NodeDesignate.Value == "NodeDesignateType4")//前一步骤领导
                    //{
                    //    FlowInstanceTransitionHistory transitionHistoryEntity = FlowInstanceTransitionHistoryService.GetEntity(flowInstanceId, node.id.Value);
                    //    UserEntity userEntity = userService.GetEntity(transitionHistoryEntity.CreateUserId);
                    //    if (string.IsNullOrEmpty(userEntity.ManagerId))
                    //    {
                    //        makerlsit = "-1";
                    //    }
                    //    else
                    //    {
                    //        makerlsit = userEntity.ManagerId;
                    //    }
                    //}
                    //else if (node.setInfo.NodeDesignate.Value == "NodeDesignateType5")//发起者部门领导
                    //{
                    //    UserEntity userEntity = userService.GetEntity(OperatorProvider.Provider.Current().UserId);
                    //    DepartmentEntity departmentEntity = departmentService.GetEntity(userEntity.DepartmentId);

                    //    if (string.IsNullOrEmpty(departmentEntity.ManagerId))
                    //    {
                    //        makerlsit = "-1";
                    //    }
                    //    else
                    //    {
                    //        makerlsit = departmentEntity.ManagerId;
                    //    }
                    //}
                    //else if (node.setInfo.NodeDesignate.Value == "NodeDesignateType6")//发起者公司领导
                    //{
                    //    UserEntity userEntity = userService.GetEntity(OperatorProvider.Provider.Current().UserId);
                    //    OrganizeEntity organizeEntity = organizeService.GetEntity(userEntity.OrganizeId);

                    //    if (string.IsNullOrEmpty(organizeEntity.ManagerId))
                    //    {
                    //        makerlsit = "-1";
                    //    }
                    //    else
                    //    {
                    //        makerlsit = organizeEntity.ManagerId;
                    //    }
                    //}
                }
                return makerlsit;
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// 将数组转化成逗号相隔的字串
        /// </summary>
        /// <param name="data"></param>
        /// <param name="Str"></param>
        /// <returns></returns>
        private string ArrwyToString(dynamic data, string Str)
        {
            string resStr = Str;
            foreach (var item in data)
            {
                if (resStr != "")
                {
                    resStr += ",";
                }
                resStr += item.Value;
            }
            return resStr;
        }


        /// <summary>
        /// 审核流程
        /// <para>李玉宝于2017-01-20 15:44:45</para>
        /// </summary>
        /// <param name="processId">The process identifier.</param>
        /// <param name="verificationData">The verification data.</param>
        public void VerificationProcess(string processId, string verificationData)
        {
            try
            {
                dynamic verificationDataJson = verificationData.ToJson();

                //驳回
                if (verificationDataJson.VerificationFinally.Value == "3")
                {
                    string _nodeId = "";
                    if (verificationDataJson.NodeRejectStep != null)
                    {
                        _nodeId = verificationDataJson.NodeRejectStep.Value;
                    }
                    NodeReject(processId, _nodeId, verificationDataJson.VerificationOpinion.Value);
                }
                else if (verificationDataJson.VerificationFinally.Value == "2")//表示不同意
                {
                    NodeVerification(processId, false, verificationDataJson.VerificationOpinion.Value);
                }
                else if (verificationDataJson.VerificationFinally.Value == "1")//表示同意
                {
                    NodeVerification(processId, true, verificationDataJson.VerificationOpinion.Value);
                }
            }
            catch
            {
                throw;
            }
        }
        

        public void Update(FlowInstance flowScheme)
        {
           Repository.Update(u => u.Id == flowScheme.Id, u => new FlowInstance());
        }

        public TableData Load(QueryFlowInstanceListReq request)
        {
            //todo:待办/已办/我的
            var result = new TableData();

            result.count = UnitWork.Find<FlowInstance>(u => u.CreateUserId == request.userid).Count();
            if (request.type == "inbox")   //待办事项
            {
                result.data = UnitWork.Find<FlowInstance>(request.page, request.limit, "CreateDate descending", null).ToList();

            }
            else if (request.type == "outbox")  //已办事项
            {
                result.data = UnitWork.Find<FlowInstance>(request.page, request.limit, "CreateDate descending", null).ToList();

            }
            else  //我的流程
            {
                result.data = UnitWork.Find<FlowInstance>(request.page, request.limit, "CreateDate descending", null).ToList();
            }

            return result;
        }
    }
}
