﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Nssol.Platypus.DataAccess.Core;
using Nssol.Platypus.DataAccess.Repositories.Interfaces;
using Nssol.Platypus.DataAccess.Repositories.Interfaces.TenantRepositories;
using Nssol.Platypus.Infrastructure;
using Nssol.Platypus.Infrastructure.Infos;
using Nssol.Platypus.Infrastructure.Options;
using Nssol.Platypus.Infrastructure.Types;
using Nssol.Platypus.Logic.Interfaces;
using Nssol.Platypus.Models;
using Nssol.Platypus.Models.TenantModels;
using Nssol.Platypus.ServiceModels.ClusterManagementModels;
using Nssol.Platypus.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Nssol.Platypus.Logic
{
    public class ClusterManagementLogic : PlatypusLogicBase, IClusterManagementLogic
    {
        // for DI
        private readonly IUserRepository userRepository;
        private readonly INodeRepository nodeRepository;
        private readonly ITensorBoardContainerRepository tensorBoardContainerRepository;
        private readonly IUnitOfWork unitOfWork;
        private readonly ILoginLogic loginLogic;
        private readonly IGitLogic gitLogic;
        private readonly IRegistryLogic registryLogic;
        private readonly IVersionLogic versionLogic;
        private readonly IClusterManagementService clusterManagementService;
        private readonly ContainerManageOptions containerOptions;
        private readonly ActiveDirectoryOptions adOptions;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ClusterManagementLogic(
            ICommonDiLogic commonDiLogic,
            IUserRepository userRepository,
            INodeRepository nodeRepository,
            ITensorBoardContainerRepository tensorBoardContainerRepository,
            IClusterManagementService clusterManagementService,
            IUnitOfWork unitOfWork,
            ILoginLogic loginLogic,
            IGitLogic gitLogic,
            IRegistryLogic registryLogic,
            IVersionLogic versionLogic,
            IOptions<ContainerManageOptions> containerOptions,
            IOptions<ActiveDirectoryOptions> adOptions
            ) : base(commonDiLogic)
        {
            this.tensorBoardContainerRepository = tensorBoardContainerRepository;
            this.userRepository = userRepository;
            this.nodeRepository = nodeRepository;
            this.clusterManagementService = clusterManagementService;
            this.loginLogic = loginLogic;
            this.gitLogic = gitLogic;
            this.registryLogic = registryLogic;
            this.versionLogic = versionLogic;
            this.unitOfWork = unitOfWork;
            this.containerOptions = containerOptions.Value;
            this.adOptions = adOptions.Value;
    }

        #region コンテナ管理

        /// <summary>
        /// クラスタ管理サービスにアクセスするための認証トークンを取得する
        /// </summary>
        private async Task<string> GetTokenAsync(bool force)
        {
            if (force)
            {
                return containerOptions.ResourceManageKey;
            }
            else
            {
                return await GetUserAccessTokenAsync();
            }
        }

        /// <summary>
        /// 全コンテナの情報を取得する
        /// </summary>
        public async Task<Result<IEnumerable<ContainerDetailsInfo>, ContainerStatus>> GetAllContainerDetailsInfosAsync()
        {
            string token = await GetTokenAsync(true);
            var result = await clusterManagementService.GetAllContainerDetailsInfosAsync(token);
            return result;
        }

        /// <summary>
        /// 特定のテナントに紐づいた全コンテナの情報を取得する
        /// </summary>
        public async Task<Result<IEnumerable<ContainerDetailsInfo>, ContainerStatus>> GetAllContainerDetailsInfosAsync(string tenantName)
        {
            //トークンは管理者ではなくユーザの物を使用する
            string token = await GetTokenAsync(false);
            var result = await clusterManagementService.GetAllContainerDetailsInfosAsync(token, tenantName);
            return result;
        }

        /// <summary>
        /// 指定したコンテナのエンドポイント付きの情報をクラスタ管理サービスに問い合わせる。
        /// </summary>
        public async Task<ContainerEndpointInfo> GetContainerEndpointInfoAsync(string containerName, string tenantName, bool force)
        {
            string token = await GetTokenAsync(force);
            if (token == null)
            {
                //トークンがない場合、エラー状態を返す
                return new ContainerEndpointInfo()
                {
                    Name = containerName,
                    Status = ContainerStatus.Failed
                };
            }

            var result = await clusterManagementService.GetContainerEndpointInfoAsync(containerName, tenantName, token);
            return result;
        }

        /// <summary>
        /// 指定したコンテナの詳細情報をクラスタ管理サービスに問い合わせる。
        /// </summary>
        public async Task<ContainerDetailsInfo> GetContainerDetailsInfoAsync(string containerName, string tenantName, bool force)
        {
            string token = await GetTokenAsync(force);
            if (token == null)
            {
                //トークンがない場合、エラー状態を返す
                return new ContainerDetailsInfo()
                {
                    Name = containerName,
                    Status = ContainerStatus.Failed
                };
            }

            var result = await clusterManagementService.GetContainerDetailsInfoAsync(containerName, tenantName, token);
            return result;
        }

        /// <summary>
        /// 指定したコンテナのステータスをクラスタ管理サービスに問い合わせる。
        /// </summary>
        public async Task<ContainerStatus> GetContainerStatusAsync(string containerName, string tenantName, bool force)
        {
            string token = await GetTokenAsync(force);
            if (token == null)
            {
                //トークンがない場合、エラー状態を返す
                return ContainerStatus.Failed;
            }

            var result = await clusterManagementService.GetContainerStatusAsync(containerName, tenantName, token);
            return result;
        }

        /// <summary>
        /// 指定したコンテナを削除する。
        /// 対象コンテナが存在しない場合はエラーになる。
        /// </summary>
        public async Task<bool> DeleteContainerAsync(ContainerType type, string containerName, string tenantName, bool force)
        {
            string token = await GetTokenAsync(force);
            if (token == null)
            {
                //トークンがない場合、エラー
                return false;
            }

            //コンテナサービスに削除を依頼
            return await clusterManagementService.DeleteContainerAsync(type, containerName, tenantName, token);
        }

        /// <summary>
        /// 指定したコンテナのログを取得する。
        /// 失敗した場合はコンテナのステータスを返す。
        /// </summary>
        public async Task<Result<System.IO.Stream, ContainerStatus>> DownloadLogAsync(string containerName, string tenantName, bool force)
        {
            string token = await GetTokenAsync(force);
            if (token == null)
            {
                //トークンがない場合、エラー
                return Result<System.IO.Stream, ContainerStatus>.CreateErrorResult(ContainerStatus.Forbidden);
            }

            //対象コンテナが稼働中なので、ログを取得する
            var logfileStream = await clusterManagementService.DownloadLogAsync(containerName, tenantName, token);
            return logfileStream;
        }

        /// <summary>
        /// 指定したテナントのイベントを取得する
        /// </summary>
        public async Task<Result<IEnumerable<ContainerEventInfo>, ContainerStatus>> GetEventsAsync(Tenant tenant, bool force)
        {
            string token = await GetTokenAsync(force);
            if (token == null)
            {
                //トークンがない場合、エラー
                return Result<IEnumerable<ContainerEventInfo>, ContainerStatus>.CreateErrorResult(ContainerStatus.Forbidden);
            }

            return await clusterManagementService.GetEventsAsync(tenant, token);
        }
        
        /// <summary>
        /// 指定したコンテナのイベントを取得する
        /// </summary>
        public async Task<Result<IEnumerable<ContainerEventInfo>, ContainerStatus>> GetEventsAsync(Tenant tenant, string containerName, bool force, bool errorOnly)
        {
            var result = await GetEventsAsync(tenant, force);

            if(result.IsSuccess)
            {
                var events = errorOnly ?
                    result.Value.Where(r => r.ContainerName == containerName && r.IsError) :
                    result.Value.Where(r => r.ContainerName == containerName);

                result = Result<IEnumerable<ContainerEventInfo>, ContainerStatus>.CreateResult(events);
            }
            return result;
        }

        #region 前処理コンテナ管理

        /// <summary>
        /// 新規に前処理用コンテナを作成する。
        /// </summary>
        /// <param name="preprocessHistory">対象の前処理履歴</param>
        /// <returns>作成したコンテナのステータス</returns>
        public async Task<Result<ContainerInfo, string>> RunPreprocessingContainerAsync(PreprocessHistory preprocessHistory)
        {
            string token = await GetUserAccessTokenAsync();
            if (token == null)
            {
                //トークンがない場合、結果はnull
                return Result<ContainerInfo, string>.CreateErrorResult("Access denied. Failed to get token to access the cluster management system.");
            }

            var registryMap = registryLogic.GetCurrentRegistryMap(preprocessHistory.Preprocess.ContainerRegistryId.Value);
            
            string tags = "-t " + preprocessHistory.Preprocess.Name; //生成されるデータのタグを設定
            foreach(var tag in preprocessHistory.InputData.Tags)
            {
                tags += " -t " + tag;
            }

            //コンテナを起動するために必要な設定値をインスタンス化
            var inputModel = new RunContainerInputModel()
            {
                ID = preprocessHistory.Id,
                TenantName = TenantName,
                LoginUser = CurrentUserInfo.Alias, //アカウントはエイリアスから指定
                Name = preprocessHistory.Name,
                ContainerImage = registryMap.Registry.GetImagePath(preprocessHistory.Preprocess.ContainerImage, preprocessHistory.Preprocess.ContainerTag),
                ScriptType = "preproc", // 実行スクリプトの指定
                Cpu = preprocessHistory.Cpu.Value,
                Memory = preprocessHistory.Memory.Value,
                Gpu = preprocessHistory.Gpu.Value,
                KqiToken = loginLogic.GenerateToken().AccessToken,
                KqiImage = "kamonohashi/cli:" + versionLogic.GetVersion(),
                LogPath= "/kqi/attach/preproc_stdout_stderr_${PREPROCESSING_ID}_${DATA_ID}.log", // 前処理履歴IDは現状ユーザーに見えないので前処理+データIDをつける
                NfsVolumeMounts = new List<NfsVolumeMountModel>()
                {
                    // 添付ファイルを保存するディレクトリ
                    // 前処理結果ディレクトリを前処理完了時にzip圧縮して添付するために使用
                    new NfsVolumeMountModel()
                    {
                        Name = "nfs-preproc-attach",
                        MountPath = "/kqi/attach",
                        SubPath = preprocessHistory.Id.ToString(),
                        Server = CurrentUserInfo.SelectedTenant.Storage.NfsServer,
                        ServerPath = CurrentUserInfo.SelectedTenant.PreprocContainerAttachedNfsPath,
                        ReadOnly = false
                    }
                },
                ContainerSharedPath = new Dictionary<string, string>()
                {
                    { "tmp", "/kqi/tmp/" },
                    { "input", "/kqi/input/" },
                    { "git", "/kqi/git/" },
                    { "output", "/kqi/output/" }
                },
                EnvList = new Dictionary<string, string>()
                {
                    { "DATA_ID", preprocessHistory.InputDataId.ToString()},
                    { "DATA_NAME", preprocessHistory.InputData.Name },
                    { "PREPROCESSING_ID", preprocessHistory.PreprocessId.ToString()},
                    { "TAGS", tags },
                    { "COMMIT_ID", preprocessHistory.Preprocess.RepositoryCommitId},
                    { "KQI_SERVER", containerOptions.WebServerUrl },
                    { "KQI_TOKEN", loginLogic.GenerateToken().AccessToken },
                    { "http_proxy", containerOptions.Proxy },
                    { "https_proxy", containerOptions.Proxy },
                    { "no_proxy", containerOptions.NoProxy },
                    { "HTTP_PROXY", containerOptions.Proxy },
                    { "HTTPS_PROXY", containerOptions.Proxy },
                    { "NO_PROXY", containerOptions.NoProxy },
                    { "COLUMNS", containerOptions.ShellColumns },
                    { "PYTHONUNBUFFERED", "true" }, // python実行時の標準出力・エラーのバッファリングをなくす
                    { "LC_ALL", "C.UTF-8"},  // python実行時のエラー回避
                    { "LANG", "C.UTF-8"}  // python実行時のエラー回避
                },
                EntryPoint = preprocessHistory.Preprocess.EntryPoint,

                ClusterManagerToken = token,
                RegistryTokenName = registryMap.RegistryTokenKey,
                IsNodePort = true
            };

            // 前処理はGitの未指定も許可するため、その判定
            if (preprocessHistory.Preprocess.RepositoryGitId != null)
            {
                long gitId = preprocessHistory.Preprocess.RepositoryGitId == -1 ?
                    CurrentUserInfo.SelectedTenant.DefaultGitId.Value : preprocessHistory.Preprocess.RepositoryGitId.Value;

                var gitEndpoint = gitLogic.GetPullUrl(preprocessHistory.Preprocess.RepositoryGitId.Value, preprocessHistory.Preprocess.RepositoryName, preprocessHistory.Preprocess.RepositoryOwner);
                if (gitEndpoint != null)
                {
                    inputModel.EnvList.Add("MODEL_REPOSITORY", gitEndpoint.FullUrl);
                    inputModel.EnvList.Add("MODEL_REPOSITORY_URL", gitEndpoint.Url);
                    inputModel.EnvList.Add("MODEL_REPOSITORY_TOKEN", gitEndpoint.Token);
                }
                else
                {
                    //Git情報は必須にしているので、無ければエラー
                    return Result<ContainerInfo, string>.CreateErrorResult("Git credential is not valid.");
                }
            }

            // ユーザの任意追加環境変数をマージする
            AddUserEnvToInputModel(preprocessHistory.OptionDic, inputModel);

            // 使用できるノードを取得する
            var nodes = GetAccessibleNode();
            if (nodes == null || nodes.Count == 0)
            {
                // デプロイ可能なノードがゼロなら、エラー扱い
                return Result<ContainerInfo, string>.CreateErrorResult("Access denied.　There is no node this tenant can use.");
            }
            else
            {
                // 制約に追加
                inputModel.ConstraintList = new Dictionary<string, List<string>>()
                {
                    { containerOptions.ContainerLabelHostName, nodes }
                };
            }

            if (string.IsNullOrEmpty(preprocessHistory.Partition) == false)
            {
                // パーティション指定があれば追加
                inputModel.ConstraintList.Add(containerOptions.ContainerLabelPartition, new List<string> { preprocessHistory.Partition });
            }

            var outModel = await clusterManagementService.RunContainerAsync(inputModel);
            if (outModel.IsSuccess == false)
            {
                return Result<ContainerInfo, string>.CreateErrorResult(outModel.Error);
            }
            return Result<ContainerInfo, string>.CreateResult(new ContainerInfo()
            {
                Name = outModel.Value.Name,
                Status = outModel.Value.Status,
                Host = outModel.Value.Host,
                Configuration = outModel.Value.Configuration
            });
        }
        #endregion

        #region Trainコンテナ管理

        /// <summary>
        /// 新規に画像認識の訓練用コンテナを作成する。
        /// </summary>
        /// <param name="trainHistory">対象の学習履歴</param>
        /// <returns>作成したコンテナのステータス</returns>
        public async Task<Result<ContainerInfo, string>> RunTrainContainerAsync(TrainingHistory trainHistory)
        {
            string token = await GetUserAccessTokenAsync();
            if (token == null)
            {
                //トークンがない場合、結果はnull
                return Result<ContainerInfo, string>.CreateErrorResult("Access denied. Failed to get token to access the cluster management system.");
            }

            long gitId = trainHistory.ModelGitId == -1 ?
                CurrentUserInfo.SelectedTenant.DefaultGitId.Value : trainHistory.ModelGitId;

            var registryMap = registryLogic.GetCurrentRegistryMap(trainHistory.ContainerRegistryId.Value);
            var gitEndpoint = gitLogic.GetPullUrl(gitId, trainHistory.ModelRepository, trainHistory.ModelRepositoryOwner);

            if (gitEndpoint == null)
            {
                //Git情報は必須にしているので、無ければエラー
                return Result<ContainerInfo, string>.CreateErrorResult("Git credential is not valid.");
            }

            var nodes = GetAccessibleNode();
            if (nodes == null || nodes.Count == 0)
            {
                //デプロイ可能なノードがゼロなら、エラー扱い
                return Result<ContainerInfo, string>.CreateErrorResult("Access denied.　There is no node this tenant can use.");
            }

            //コンテナを起動するために必要な設定値をインスタンス化
            var inputModel = new RunContainerInputModel()
            {
                ID = trainHistory.Id,
                TenantName = TenantName,
                LoginUser = CurrentUserInfo.Alias, //アカウントはエイリアスから指定
                Name = trainHistory.Key,
                ContainerImage = registryMap.Registry.GetImagePath(trainHistory.ContainerImage, trainHistory.ContainerTag),
                ScriptType = "training", 
                Cpu = trainHistory.Cpu,
                Memory = trainHistory.Memory,
                Gpu = trainHistory.Gpu,
                KqiImage = "kamonohashi/cli:" + versionLogic.GetVersion(),
                KqiToken = loginLogic.GenerateToken().AccessToken,
                LogPath = "/kqi/attach/training_stdout_stderr_${TRAINING_ID}.log",
                NfsVolumeMounts = new List<NfsVolumeMountModel>()
                {
                    // 結果保存するディレクトリ
                    new NfsVolumeMountModel()
                    {
                        Name = "nfs-output",
                        MountPath = "/kqi/output",
                        SubPath = trainHistory.Id.ToString(),
                        Server = CurrentUserInfo.SelectedTenant.Storage.NfsServer,
                        ServerPath = CurrentUserInfo.SelectedTenant.TrainingContainerOutputNfsPath,
                        ReadOnly = false
                    },
                    // 添付ファイルを保存するディレクトリ
                    // 学習結果ディレクトリを学習完了時にzip圧縮して添付するために使用
                    new NfsVolumeMountModel()
                    {
                        Name = "nfs-attach",
                        MountPath = "/kqi/attach",
                        SubPath = trainHistory.Id.ToString(),
                        Server = CurrentUserInfo.SelectedTenant.Storage.NfsServer,
                        ServerPath = CurrentUserInfo.SelectedTenant.TrainingContainerAttachedNfsPath,
                        ReadOnly = false
                    }
                },
                ContainerSharedPath = new Dictionary<string, string>()
                {
                    { "tmp", "/kqi/tmp/" },
                    { "input", "/kqi/input/" },
                    { "git", "/kqi/git/" }
                },
                EnvList = new Dictionary<string, string>()
                {
                    { "DATASET_ID", trainHistory.DataSetId.ToString()},
                    { "TRAINING_ID", trainHistory.Id.ToString()},
                    { "PARENT_ID", trainHistory.ParentId?.ToString()},
                    { "MODEL_REPOSITORY", gitEndpoint.FullUrl},
                    { "MODEL_REPOSITORY_URL", gitEndpoint.Url},
                    { "MODEL_REPOSITORY_TOKEN", gitEndpoint.Token},
                    { "COMMIT_ID", trainHistory.ModelCommitId},
                    { "KQI_SERVER", containerOptions.WebServerUrl },
                    { "KQI_TOKEN", loginLogic.GenerateToken().AccessToken },
                    { "http_proxy", containerOptions.Proxy },
                    { "https_proxy", containerOptions.Proxy },
                    { "no_proxy", containerOptions.NoProxy },
                    { "HTTP_PROXY", containerOptions.Proxy },
                    { "HTTPS_PROXY", containerOptions.Proxy },
                    { "NO_PROXY", containerOptions.NoProxy },
                    { "COLUMNS", containerOptions.ShellColumns },
                    { "PYTHONUNBUFFERED", "true" }, // python実行時の標準出力・エラーのバッファリングをなくす
                    { "LC_ALL", "C.UTF-8"},  // python実行時のエラー回避
                    { "LANG", "C.UTF-8"}  // python実行時のエラー回避
                },
                EntryPoint = trainHistory.EntryPoint,

                PortMappings = new PortMappingModel[]
                {
                    new PortMappingModel() { Protocol = "TCP", Port = 22, TargetPort = 22, Name = "ssh" },
                },
                ClusterManagerToken = token,
                RegistryTokenName = registryMap.RegistryTokenKey,
                IsNodePort = true
            };
            // 親を指定した場合は親の出力結果を/kqi/parentにマウント
            if (trainHistory.ParentId != null)
            {
                inputModel.NfsVolumeMounts.Add(new NfsVolumeMountModel()
                {
                    Name = "nfs-parent",
                    MountPath = "/kqi/parent",
                    SubPath = trainHistory.ParentId.ToString(),
                    Server = CurrentUserInfo.SelectedTenant.Storage.NfsServer,
                    ServerPath = CurrentUserInfo.SelectedTenant.TrainingContainerOutputNfsPath,
                    ReadOnly = true
                });
            }

            // ユーザの任意追加環境変数をマージする
            AddUserEnvToInputModel(trainHistory.OptionDic, inputModel);

            //使用できるノードを制約に追加
            inputModel.ConstraintList = new Dictionary<string, List<string>>()
            {
                { containerOptions.ContainerLabelHostName, nodes }
            };

            if (string.IsNullOrEmpty(trainHistory.Partition) == false)
            {
                // パーティション指定があれば追加
                inputModel.ConstraintList.Add(containerOptions.ContainerLabelPartition, new List<string> { trainHistory.Partition });
            }

            var outModel = await clusterManagementService.RunContainerAsync(inputModel);
            if (outModel.IsSuccess == false)
            {
                return Result<ContainerInfo, string>.CreateErrorResult(outModel.Error);
            }
            var port = outModel.Value.PortMappings.Find(p => p.Name == "ssh");
            return Result<ContainerInfo, string>.CreateResult(new ContainerInfo()
            {
                Name = outModel.Value.Name,
                Status = outModel.Value.Status,
                Host = outModel.Value.Host,
                Port = port.NodePort,
                Configuration = outModel.Value.Configuration
            });
        }

        /// <summary>
        /// 新規に画像認識の推論用コンテナを作成する。
        /// </summary>
        /// <param name="inferenceHistory">対象の推論履歴</param>
        /// <returns>作成したコンテナのステータス</returns>
        public async Task<Result<ContainerInfo, string>> RunInferenceContainerAsync(InferenceHistory inferenceHistory)
        {
            string token = await GetUserAccessTokenAsync();
            if (token == null)
            {
                //トークンがない場合、結果はnull
                return Result<ContainerInfo, string>.CreateErrorResult("Access denied. Failed to get token to access the cluster management system.");
            }

            long gitId = inferenceHistory.ModelGitId == -1 ?
                CurrentUserInfo.SelectedTenant.DefaultGitId.Value : inferenceHistory.ModelGitId.Value;

            var registryMap = registryLogic.GetCurrentRegistryMap(inferenceHistory.ContainerRegistryId.Value);
            var gitEndpoint = gitLogic.GetPullUrl(gitId, inferenceHistory.ModelRepository, inferenceHistory.ModelRepositoryOwner);

            if (gitEndpoint == null)
            {
                //Git情報は必須にしているので、無ければエラー
                return Result<ContainerInfo, string>.CreateErrorResult("Git credential is not valid.");
            }

            var nodes = GetAccessibleNode();
            if (nodes == null || nodes.Count == 0)
            {
                //デプロイ可能なノードがゼロなら、エラー扱い
                return Result<ContainerInfo, string>.CreateErrorResult("Access denied.　There is no node this tenant can use.");
            }

           
            //コンテナを起動するために必要な設定値をインスタンス化
            var inputModel = new RunContainerInputModel()
            {
                ID = inferenceHistory.Id,
                TenantName = TenantName,
                LoginUser = CurrentUserInfo.Alias, //アカウントはエイリアスから指定
                Name = inferenceHistory.Key,
                ContainerImage = registryMap.Registry.GetImagePath(inferenceHistory.ContainerImage, inferenceHistory.ContainerTag),
                ScriptType = "inference", 
                Cpu = inferenceHistory.Cpu,
                Memory = inferenceHistory.Memory,
                Gpu = inferenceHistory.Gpu,
                KqiImage = "kamonohashi/cli:" + versionLogic.GetVersion(),
                KqiToken = loginLogic.GenerateToken().AccessToken,
                LogPath = "/kqi/attach/inference_stdout_stderr_${INFERENCE_ID}.log",
                NfsVolumeMounts = new List<NfsVolumeMountModel>()
                {
                    // 結果保存するディレクトリ
                    new NfsVolumeMountModel()
                    {
                        Name = "nfs-output",
                        MountPath = "/kqi/output",
                        SubPath = inferenceHistory.Id.ToString(),
                        Server = CurrentUserInfo.SelectedTenant.Storage.NfsServer,
                        ServerPath = CurrentUserInfo.SelectedTenant.InferenceContainerOutputNfsPath,
                        ReadOnly = false
                    },
                    // 添付ファイルを保存するディレクトリ
                    // 学習結果ディレクトリを学習完了時にzip圧縮して添付するために使用
                    new NfsVolumeMountModel()
                    {
                        Name = "nfs-attach",
                        MountPath = "/kqi/attach",
                        SubPath = inferenceHistory.Id.ToString(),
                        Server = CurrentUserInfo.SelectedTenant.Storage.NfsServer,
                        ServerPath = CurrentUserInfo.SelectedTenant.InferenceContainerAttachedNfsPath,
                        ReadOnly = false
                    }
                },
                ContainerSharedPath = new Dictionary<string, string>()
                {
                    { "tmp", "/kqi/tmp/" },
                    { "input", "/kqi/input/" },
                    { "git", "/kqi/git/" }
                },
                EnvList = new Dictionary<string, string>()
                {
                    { "DATASET_ID", inferenceHistory.DataSetId.ToString()},
                    { "INFERENCE_ID", inferenceHistory.Id.ToString()},
                    { "PARENT_ID", inferenceHistory.ParentId?.ToString()},
                    { "MODEL_REPOSITORY", gitEndpoint.FullUrl},
                    { "MODEL_REPOSITORY_URL", gitEndpoint.Url},
                    { "MODEL_REPOSITORY_TOKEN", gitEndpoint.Token},
                    { "COMMIT_ID", inferenceHistory.ModelCommitId},
                    { "KQI_SERVER", containerOptions.WebServerUrl },
                    { "KQI_TOKEN", loginLogic.GenerateToken().AccessToken },
                    { "http_proxy", containerOptions.Proxy },
                    { "https_proxy", containerOptions.Proxy },
                    { "no_proxy", containerOptions.NoProxy },
                    { "HTTP_PROXY", containerOptions.Proxy },
                    { "HTTPS_PROXY", containerOptions.Proxy },
                    { "NO_PROXY", containerOptions.NoProxy },
                    { "COLUMNS", containerOptions.ShellColumns },
                    { "PYTHONUNBUFFERED", "true" }, // python実行時の標準出力・エラーのバッファリングをなくす
                    { "LC_ALL", "C.UTF-8"},  // python実行時のエラー回避
                    { "LANG", "C.UTF-8"}  // python実行時のエラー回避
                },
                EntryPoint = inferenceHistory.EntryPoint,

                PortMappings = new PortMappingModel[]
                {
                    new PortMappingModel() { Protocol = "TCP", Port = 22, TargetPort = 22, Name = "ssh" },
                },
                ClusterManagerToken = token,
                RegistryTokenName = registryMap.RegistryTokenKey,
                IsNodePort = true
            };
            // 親を指定した場合は親の出力結果を/kqi/parentにマウント
            // 推論ジョブにおける親ジョブは学習ジョブとなるので、SubPathとServerPathの指定に注意
            if (inferenceHistory.ParentId != null)
            {
                inputModel.NfsVolumeMounts.Add(new NfsVolumeMountModel()
                {
                    Name = "nfs-parent",
                    MountPath = "/kqi/parent",
                    SubPath = inferenceHistory.ParentId.ToString(),
                    Server = CurrentUserInfo.SelectedTenant.Storage.NfsServer,
                    ServerPath = CurrentUserInfo.SelectedTenant.TrainingContainerOutputNfsPath,
                    ReadOnly = true
                });
            }

            // ユーザの任意追加環境変数をマージする
            AddUserEnvToInputModel(inferenceHistory.OptionDic, inputModel);

            //使用できるノードを制約に追加
            inputModel.ConstraintList = new Dictionary<string, List<string>>()
            {
                { containerOptions.ContainerLabelHostName, nodes }
            };

            if (string.IsNullOrEmpty(inferenceHistory.Partition) == false)
            {
                // パーティション指定があれば追加
                inputModel.ConstraintList.Add(containerOptions.ContainerLabelPartition, new List<string> { inferenceHistory.Partition });
            }

            var outModel = await clusterManagementService.RunContainerAsync(inputModel);
            if (outModel.IsSuccess == false)
            {
                return Result<ContainerInfo, string>.CreateErrorResult(outModel.Error);
            }
            var port = outModel.Value.PortMappings.Find(p => p.Name == "ssh");
            return Result<ContainerInfo, string>.CreateResult(new ContainerInfo()
            {
                Name = outModel.Value.Name,
                Status = outModel.Value.Status,
                Host = outModel.Value.Host,
                Port = port.NodePort,
                Configuration = outModel.Value.Configuration
            });
        }
        #endregion

        #region TensorBoardコンテナ管理
        /// <summary>
        /// 新規にTensorBoard表示用のコンテナを作成する。
        /// 成功した場合は作成結果が、失敗した場合はnullが返る。
        /// </summary>
        /// <param name="trainingHistory">対象の学習履歴</param>
        /// <returns>作成したコンテナのステータス</returns>
        public async Task<ContainerInfo> RunTensorBoardContainerAsync(TrainingHistory trainingHistory)
        {
            //コンテナ名は自動生成
            //使用できる文字など、命名規約はコンテナ管理サービス側によるが、
            //ユーザ入力値検証の都合でどうせ決め打ちしないといけないので、ロジック層で作ってしまう
            string tenantId = CurrentUserInfo.SelectedTenant.Id.ToString("0000");
            string containerName = $"tensorboard-{tenantId}-{trainingHistory.Id}-{DateTime.Now.ToString("yyyyMMddHHmmssffffff")}";
            var registryMap = registryLogic.GetCurrentRegistryMap(trainingHistory.ContainerRegistryId.Value);

            string token = await GetUserAccessTokenAsync();
            if(token == null)
            {
                //トークンがない場合、結果はnull
                return new ContainerInfo() { Status = ContainerStatus.Forbidden };
            }

            var nodes = GetAccessibleNode();
            if(nodes == null || nodes.Count == 0)
            {
                //デプロイ可能なノードがゼロなら、エラー扱い
                return new ContainerInfo() { Status = ContainerStatus.Forbidden };
            }

            //コンテナを起動するために必要な設定値をインスタンス化
            var inputModel = new RunContainerInputModel()
            {
                ID = trainingHistory.Id,
                TenantName = TenantName,
                LoginUser = CurrentUserInfo.Alias, //アカウントはエイリアスから指定
                Name = containerName,
                ContainerImage = "tensorflow/tensorflow",
                ScriptType = "tensorboard",
                Cpu = 1,
                Memory = 1, //メモリは1GBで仮決め
                Gpu = 0,
                KqiImage = "kamonohashi/cli:" + versionLogic.GetVersion(),
                NfsVolumeMounts = new List<NfsVolumeMountModel>()
                {
                    // 結果が保存されているディレクトリ
                    new NfsVolumeMountModel()
                    {
                        Name = "nfs-output",
                        MountPath = "/kqi/output",
                        SubPath = trainingHistory.Id.ToString(),
                        Server = CurrentUserInfo.SelectedTenant.Storage.NfsServer,
                        ServerPath = CurrentUserInfo.SelectedTenant.TrainingContainerOutputNfsPath
                    }
                },
                EnvList = new Dictionary<string, string>()
                {
                    { "KQI_SERVER", containerOptions.WebServerUrl },
                    { "KQI_TOKEN", loginLogic.GenerateToken().AccessToken },
                    { "http_proxy", containerOptions.Proxy },
                    { "https_proxy", containerOptions.Proxy },
                    { "no_proxy", containerOptions.NoProxy },
                    { "HTTP_PROXY", containerOptions.Proxy },
                    { "HTTPS_PROXY", containerOptions.Proxy },
                    { "NO_PROXY", containerOptions.NoProxy },
                    { "PYTHONUNBUFFERED", "true" }, // python実行時の標準出力・エラーのバッファリングをなくす
                    { "LC_ALL", "C.UTF-8"}, // python実行時のエラー回避
                    { "LANG", "C.UTF-8"}  // python実行時のエラー回避
                },
                ConstraintList = new Dictionary<string, List<string>>() {
                    { containerOptions.ContainerLabelHostName, nodes }, //使用できるノードを取得し、制約に追加
                    { containerOptions.ContainerLabelTensorBoardEnabled, new List<string> { "true" } } // tensorboardの実行が許可されているサーバでのみ実行,
                },
                PortMappings = new PortMappingModel[]
                {
                    new PortMappingModel() { Protocol = "TCP", Port = 6006, TargetPort = 6006, Name = "tensorboard" }
                },
                ClusterManagerToken = token,
                RegistryTokenName = registryMap.RegistryTokenKey,
                IsNodePort = true //ランダムポート指定。アクセス先ポートが動的に決まるようになる。
            };

            var outModel = await clusterManagementService.RunContainerAsync(inputModel);

            if (outModel.IsSuccess == false)
            {
                return new ContainerInfo() { Status = ContainerStatus.Failed };
            }
            var port = outModel.Value.PortMappings.Find(p => p.Name == "tensorboard");
            return new ContainerInfo()
            {
                Name = containerName,
                Status = outModel.Value.Status,
                Host = outModel.Value.Host,
                Port = port.NodePort,
                Configuration = outModel.Value.Configuration
            };
        }

        /// <summary>
        /// 指定したTensorBoardコンテナのステータスをクラスタ管理サービスに問い合わせ、結果でDBを更新する。
        /// </summary>
        /// <remark>
        /// TensorBoardコンテナの場合、以下の理由から、エラーが発生した場合は即DBからも削除してしまう。
        /// ・履歴管理をする必要がない
        /// ・名前に時刻が入っているので、もしコンテナが残っていても次回起動には支障がない。
        /// </remark>
        public async Task<ContainerStatus> SyncContainerStatusAsync(TensorBoardContainer container, bool force)
        {
            ContainerStatus result;
            if (string.IsNullOrEmpty(container.Host))
            {
                //ホストが決まっていない＝リソースに空きがなくて、待っている状態

                var info = await GetContainerEndpointInfoAsync(container.Name, CurrentUserInfo.SelectedTenant.Name, false);
                result = info.Status;
                var endpoint = info.EndPoints?.FirstOrDefault(e => e.Key == "tensorboard");
                if (endpoint != null)
                {
                    //ノードが立ったので、ポート情報を更新する
                    //どんな状態のインスタンスが引数で与えられるかわからないので、改めて取得して更新
                    var nextStatusContainer = await tensorBoardContainerRepository.GetByIdAsync(container.Id);
                    nextStatusContainer.Host = endpoint.Host;
                    nextStatusContainer.PortNo = endpoint.Port;
                    nextStatusContainer.Status = result.Name;
                    tensorBoardContainerRepository.Update(nextStatusContainer);
                    unitOfWork.Commit();

                    return info.Status;
                }
                //まだホストが決まっていない場合は、後段処理を実行（対象コンテナがないかもしれないから）
            }
            else
            {
                result = await GetContainerStatusAsync(container.Name, container.Tenant.Name, force);
            }

            
            if (result.Exist() == false)
            {
                //コンテナがすでに停止しているので、ログを出した後でDBから対象レコードを削除
                LogInformation($"ステータス {result.Name} のTensorBoardコンテナ {container.Id} {container.Name} を削除します。");
                tensorBoardContainerRepository.Delete(container, force);
                unitOfWork.Commit();
            }
            else
            {
                bool updateResult = tensorBoardContainerRepository.UpdateStatus(container.Id, result.Name, true);
                if(updateResult == false)
                {
                    //削除対象がすでに消えていた場合
                    return ContainerStatus.None;
                }
                unitOfWork.Commit();
            }
            return result;
        }
        #endregion

        /// <summary>
        /// ユーザの任意追加環境変数をマージする
        /// </summary>
        private static void AddUserEnvToInputModel(Dictionary<string, string> optionDic, RunContainerInputModel inputModel)
        {
            if (optionDic.Count > 0)
            {
                foreach (var env in optionDic)
                {
                    // ユーザー指定環境変数とappsettingsの環境変数を結合

                    string value = env.Value ?? ""; //nullは空文字と見なす

                    if (inputModel.EnvList.ContainsKey(env.Key))
                    {
                        inputModel.EnvList[env.Key] = value; //あればユーザ指定の値で上書き
                    }
                    else
                    {
                        inputModel.EnvList.Add(env.Key, value); //なければ追加
                    }
                }
            }
        }


        #endregion

        #region クラスタ管理

        /// <summary>
        /// 全ノード情報を取得する。
        /// 取得失敗した場合はnullが返る。
        /// </summary>
        public async Task<IEnumerable<NodeInfo>> GetAllNodesAsync()
        {
            var registeredNodeNames = nodeRepository.GetAll().Select(n => n.Name).ToList();
            return await clusterManagementService.GetAllNodesAsync(registeredNodeNames);
        }

        /// <summary>
        /// ノード単位のパーティションリストを取得する
        /// </summary>
        public async Task<Result<Dictionary<string, string>, string>> GetNodePartitionMapAsync()
        {
            string labelPartition = containerOptions.ContainerLabelPartition;
            var registeredNodeNames = nodeRepository.GetAll().Select(n => n.Name).ToList();
            return await clusterManagementService.GetNodeLabelMapAsync(labelPartition, registeredNodeNames);
        }

        /// <summary>
        /// パーティションを更新する
        /// </summary>
        /// <param name="nodeName">ノード名</param>
        /// <param name="labelValue">ノード値</param>
        /// <returns>更新結果、更新できた場合、true</returns>
        public async Task<bool> UpdatePartitionLabelAsync(string nodeName, string labelValue)
        {
            return await this.clusterManagementService.SetNodeLabelAsync(nodeName, containerOptions.ContainerLabelPartition, labelValue);
        }

        /// <summary>
        /// TensorBoardの実行可否設定を更新する
        /// </summary>
        /// <param name="nodeName">ノード名</param>
        /// <param name="enabled">実行可否</param>
        /// <returns>更新結果、更新できた場合、true</returns>
        public async Task<bool> UpdateTensorBoardEnabledLabelAsync(string nodeName, bool enabled)
        {
            string value = enabled ? "true" : "";
            return await this.clusterManagementService.SetNodeLabelAsync(nodeName, containerOptions.ContainerLabelTensorBoardEnabled, value);
        }

        /// <summary>
        /// 指定されたテナントのクォータ設定をクラスタに反映させる。
        /// </summary>
        /// <returns>更新結果、更新できた場合、true</returns>
        public async Task<bool> SetQuotaAsync(Tenant tenant)
        {
            return await this.clusterManagementService.SetQuotaAsync(
                tenant.Name,
                tenant.LimitCpu == null ? 0 : tenant.LimitCpu.Value,
                tenant.LimitMemory == null ? 0 : tenant.LimitMemory.Value,
                tenant.LimitGpu == null ? 0 : tenant.LimitGpu.Value);
        }

        #endregion

        #region 権限管理

        /// <summary>
        /// クラスタ管理サービス上で、指定したユーザ＆テナントにコンテナレジストリを登録する。
        /// idempotentを担保。
        /// </summary>
        public async Task<bool> RegistRegistryToTenantAsync(string selectedTenantName, UserTenantRegistryMap userRegistryMap)
        {
            if (userRegistryMap == null)
            {
                return false;
            }
            //初回登録時など、まだパスワードが設定されていなかったら、登録はしない。
            if (string.IsNullOrEmpty(userRegistryMap.RegistryPassword))
            {
                return true; //正常系扱い
            }
            string dockerCfg = registryLogic.GetDockerCfgAuthString(userRegistryMap);
            if(dockerCfg == null)
            {
                return false;
            }
            var inModel = new RegistRegistryTokenInputModel()
            {
                TenantName = selectedTenantName,
                RegistryTokenKey = userRegistryMap.RegistryTokenKey,
                DockerCfgAuthString = dockerCfg,
                Url = userRegistryMap.Registry.RegistryUrl
            };
            return await clusterManagementService.RegistRegistryTokenyAsync(inModel);
        }

        /// <summary>
        /// 指定したテナントを作成する。
        /// 既にある場合は何もしない。
        /// </summary>
        public async Task<bool> RegistTenantAsync(string tenantName)
        {
            return await clusterManagementService.RegistTenantAsync(tenantName);
        }

        /// <summary>
        /// ログイン中のユーザ＆テナントに対する、クラスタ管理サービスにアクセスするためのトークンを取得する。
        /// 存在しない場合、新規に作成する。
        /// </summary>
        public async Task<string> GetUserAccessTokenAsync()
        {
            string token = userRepository.GetClusterToken(CurrentUserInfo.Id, CurrentUserInfo.SelectedTenant.Id);

            if (token == null)
            {
                //トークンがない場合、新規に作成する
                //作成時の名前はUserNameではなくAliasを使う
                if (string.IsNullOrEmpty(CurrentUserInfo.Alias))
                {
                    //Aliasがない場合は、乱数で作成する
                    string alias = Util.GenerateRandamString(10);

                    //DBに保存
                    var user = await userRepository.SetAliasAsync(CurrentUserInfo.Id, alias);
                    LogInformation($"Set alias {alias} to {CurrentUserInfo.Id}:{CurrentUserInfo.Name}");
                    unitOfWork.Commit(); //仮にクラスタトークンの生成に失敗しても、エイリアスは保存して、ロールバックはしない

                    CurrentUserInfo.Alias = alias;
                }
                token = await clusterManagementService.RegistUserAsync(TenantName, CurrentUserInfo.Alias);

                if(token == null)
                {
                    //トークン生成に失敗
                    return null;
                }

                //新規トークンをDBへ登録
                userRepository.SetClusterToken(CurrentUserInfo.Id, CurrentUserInfo.SelectedTenant.Id, token);
                unitOfWork.Commit();
            }

            return token;
        }

        /// <summary>
        /// 指定したテナントを抹消(削除)する。
        /// </summary>
        public async Task<bool> EraseTenantAsync(string tenantName)
        {
            return await clusterManagementService.EraseTenantAsync(tenantName);
        }

        /// <summary>
        /// 現在接続中のテナントが使用できるノード一覧を取得する
        /// </summary>
        private List<string> GetAccessibleNode()
        {
            return nodeRepository.GetAccessibleNodes(CurrentUserInfo.SelectedTenant.Id).Select(n => n.Name).ToList();
        }
        #endregion

        #region WebSocket通信
        /// <summary>
        /// ブラウザとのWebSocket接続および、KubernetesとのWebSocket接続を確立する。
        /// そしてブラウザからのメッセージを待機し、メッセージを受信した際にはその内容をKubernetesに送信する。
        /// </summary>
        public async Task ConnectKubernetesWebSocketAsync(HttpContext context)
        {
            // ブラウザとのWebSocket接続を確立
            WebSocket browserWebSocket = await context.WebSockets.AcceptWebSocketAsync();


            // リクエストから、job名、tenant名を取得
            string jobName = context.Request.Query["jobName"];
            string tenantName = context.Request.Query["tenantName"];

            var containerOptions = CommonDiLogic.DynamicDi<IOptions<ContainerManageOptions>>();
            var token = containerOptions.Value.ResourceManageKey;   // 全テナントにアクセス可能な状態。CurrentUserInfoがnullであるため(Claim情報が無いため)、GetUserAccessTokenAsync()が使えない

            // KubernetesとのWebSocket接続を確立
            var kubernetesService = CommonDiLogic.DynamicDi<Services.Interfaces.IClusterManagementService>();
            var result = await kubernetesService.ConnectWebSocketAsync(jobName, tenantName, token);

            // 確立に失敗した場合は、ブラウザとの接続を切断
            if (result.Error != null)
            {
                var buff = new List<byte>(System.Text.Encoding.ASCII.GetBytes("\"" + jobName + "\" not found.\r\nConnection Closed."));
                await browserWebSocket.SendAsync(buff.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);

                await browserWebSocket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "Kubernetes error", CancellationToken.None); ;
                return;
            }
            ClientWebSocket kubernetesWebSocket = result.Value; 

            // Kubernetesの情報を中継する処理を、別スレッドで起動。
            var task = CommunicateKubernetesAsync(kubernetesWebSocket, browserWebSocket);

            // ブラウザからの入力を中継
            while (browserWebSocket.State == WebSocketState.Open)
            {
                try
                {
                    // ブラウザからメッセージ待受(通常入力時は、ブラウザ上コンソールから1文字単位で送られてくる。ペーストした場合は一度に複数文字送られてくる)
                    var buff = new ArraySegment<byte>(new byte[1024]);
                    var ret = await browserWebSocket.ReceiveAsync(buff, System.Threading.CancellationToken.None);

                    var sendK8sBuffer = new List<byte>();
                    sendK8sBuffer.Insert(0, 0); // stdin prefix(0x00)を追加

                    for(int i=0; i < ret.Count; i++)
                    {
                        sendK8sBuffer.Add(buff[i]);
                    }
                    await kubernetesWebSocket.SendAsync(sendK8sBuffer.ToArray(), WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                catch
                {
                    // ブラウザが異常終了した場合、Kubernetes側との接続が切れた場合
                    browserWebSocket.Dispose();
                    kubernetesWebSocket.Dispose();
                    return;
                }
            }

            // ブラウザが、切断要求を行った場合、
            await browserWebSocket.CloseOutputAsync(browserWebSocket.CloseStatus.Value, browserWebSocket.CloseStatusDescription, CancellationToken.None);
            await kubernetesWebSocket.CloseOutputAsync(browserWebSocket.CloseStatus.Value, browserWebSocket.CloseStatusDescription, CancellationToken.None);
            browserWebSocket.Dispose();
            kubernetesWebSocket.Dispose();
            return;
        }

        /// <summary>
        /// Kubernetesからのメッセージを待機し、メッセージを受信した際にはその内容をブラウザに送信する。
        /// </summary>
        private static async Task CommunicateKubernetesAsync(ClientWebSocket kubernetesWebSocket, WebSocket browserWebSocket)
        {
            while (kubernetesWebSocket.State == WebSocketState.Open)
            {
                //Kubernetesからメッセージ待受。メッセージを受信した際には、Browser側に送信
                var receivedBuffer = new ArraySegment<byte>(new byte[1024]);
                await kubernetesWebSocket.ReceiveAsync(receivedBuffer, CancellationToken.None);
                var sendBuffer = new List<byte>();
                for (int i = 1; i != receivedBuffer.Count; i++) // stdout prefix(0x01)を読み飛ばし
                {
                    byte b = receivedBuffer[i];
                    if (b != 0)  // 文字情報のみ抜き出し
                        sendBuffer.Add(b);
                }
                await browserWebSocket.SendAsync(sendBuffer.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
            }

            // Kubernetesとの接続が切れた場合(ジョブが終了した場合等)
            await kubernetesWebSocket.CloseOutputAsync(kubernetesWebSocket.CloseStatus.Value, kubernetesWebSocket.CloseStatusDescription, CancellationToken.None);
            await browserWebSocket.CloseOutputAsync(kubernetesWebSocket.CloseStatus.Value, kubernetesWebSocket.CloseStatusDescription, CancellationToken.None);
        }
        #endregion
    }
}
