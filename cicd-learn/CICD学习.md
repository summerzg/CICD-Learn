# 用 GitHub + Azure $50 打通完整 CI/CD

---

## 一、资源规划（控制在 $50 以内）

```
资源                        规格              月费用    用途
──────────────────────────────────────────────────────────────────
Azure Container Registry    Basic Tier        ~$5       私有镜像仓库
Azure VM (DEV/TEST)         B2s (2C/4G)       ~$30      运行 dev + test 环境
Azure VM (UAT)              B1s (1C/1G)       ~$8       UAT 环境
SonarCloud                  免费 (公开仓库)    $0        代码质量扫描
GitHub Actions              免费额度           $0        CI/CD 引擎
──────────────────────────────────────────────────────────────────
合计                                           ~$43      剩余 $7 余量
```

> **省钱技巧**: 不用的时候关机（VM 停止后只收存储费 ~$0.5/月）。学习期间按需开关，实际花费远低于 $43。

---

## 二、整体架构

```
┌──────────────────────────────────────────────────────────────┐
│                        GitHub                                │
│  Repository                                                  │
│  ├── GitHub Actions (CI/CD 引擎，免费)                        │
│  └── Environments: dev / test / uat / production             │
└────────────────────┬─────────────────────────────────────────┘
                     │ push image
                     ▼
┌──────────────────────────────────────────────────────────────┐
│              Azure Container Registry (ACR)                  │
│  myapp:dev-{sha}  /  myapp:1.0.0-rc.1  /  myapp:1.0.0       │
└───────┬──────────────────────┬───────────────────────────────┘
        │ pull                 │ pull
        ▼                      ▼
┌───────────────┐    ┌──────────────────┐    ┌───────────────┐
│  Azure VM 1   │    │   Azure VM 1     │    │  Azure VM 2   │
│  (B2s)        │    │   (B2s)          │    │  (B1s)        │
│  DEV 环境     │    │   TEST 环境      │    │  UAT 环境     │
│  port: 3000   │    │   port: 3001     │    │  port: 80     │
└───────────────┘    └──────────────────┘    └───────────────┘
   同一台 VM，不同 Docker 网络隔离                  独立 VM
```

---

## 三、分阶段学习计划（共 8 周）

---

### 第一阶段（Week 1）：搭环境 + 跑通第一条流水线

**目标**: push 代码 → GitHub Actions 自动运行单测

#### 1.1 创建示例项目

选一个你熟悉的语言，用最简单的 Web 应用：

```bash
# Node.js 示例（也可以用 Python/Java/Go）
mkdir cicd-demo && cd cicd-demo
npm init -y
npm install express
npm install --save-dev jest

# 创建 src/app.js
# 创建 src/app.test.js
# 创建 Dockerfile
git init && git remote add origin https://github.com/yourname/cicd-demo
```

#### 1.2 第一条 GitHub Actions 流水线

```yaml
# .github/workflows/pr-check.yml
name: PR Check

on:
  pull_request:
    branches: [main, develop]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-node@v4
        with:
          node-version: "20"
          cache: "npm" # 缓存依赖，加速后续运行

      - run: npm ci
      - run: npm test -- --coverage

      - name: Upload coverage report
        uses: actions/upload-artifact@v4
        with:
          name: coverage
          path: coverage/
```

**验证**: 创建一个 PR，观察 Actions Tab 里流水线运行过程。

---

### 第二阶段（Week 2）：接入 SonarCloud 代码质量门禁

**目标**: 覆盖率不足或有 Bug 时，PR 被自动拦截

#### 2.1 注册 SonarCloud

1. 访问 [sonarcloud.io](https://sonarcloud.io)，用 GitHub 账号登录（**公开仓库免费**）
2. 导入你的仓库
3. 在 SonarCloud 界面获取 `SONAR_TOKEN`
4. 在 GitHub → Settings → Secrets → Actions 添加 `SONAR_TOKEN`

#### 2.2 更新流水线

```yaml
# .github/workflows/pr-check.yml 在 test job 后新增

sonar-scan:
  needs: test
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v4
      with:
        fetch-depth: 0 # SonarCloud 需要完整 git 历史做增量分析

    - name: Download coverage
      uses: actions/download-artifact@v4
      with:
        name: coverage
        path: coverage/

    - name: SonarCloud Scan
      uses: SonarSource/sonarcloud-github-action@master
      env:
        SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
```

```properties
# sonar-project.properties（放项目根目录）
sonar.projectKey=yourname_cicd-demo
sonar.organization=yourname
sonar.javascript.lcov.reportPaths=coverage/lcov.info

# Quality Gate: 新增代码覆盖率 < 80% 则失败（在 SonarCloud 界面配置）
```

**验证**: 故意写一段没有测试的代码提 PR，观察 SonarCloud Quality Gate 失败拦截效果。

---

### 第三阶段（Week 3）：构建 Docker 镜像推送到 ACR

**目标**: merge to develop → 自动构建镜像推送到 Azure Container Registry

#### 3.1 创建 ACR

```bash
# Azure CLI（在 Azure Cloud Shell 里执行，免安装）
az group create --name cicd-demo-rg --location eastasia

az acr create \
  --resource-group cicd-demo-rg \
  --name myacrcicd \           # 全局唯一名称
  --sku Basic \
  --admin-enabled true

# 获取登录凭证
az acr credential show --name myacrcicd
# 记录 username 和 password
```

#### 3.2 写好 Dockerfile

```dockerfile
# 多阶段构建，生产镜像只包含运行时
FROM node:20-alpine AS builder
WORKDIR /app
COPY package*.json ./
RUN npm ci --only=production

FROM node:20-alpine AS runtime
WORKDIR /app
COPY --from=builder /app/node_modules ./node_modules
COPY src/ ./src/
EXPOSE 3000
CMD ["node", "src/app.js"]
```

#### 3.3 CI 流水线（push to develop）

```yaml
# .github/workflows/ci.yml
name: CI - Build and Deploy DEV

on:
  push:
    branches: [develop]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: "20", cache: "npm" }
      - run: npm ci && npm test -- --coverage

  build-and-push:
    needs: test
    runs-on: ubuntu-latest
    outputs:
      image-tag: ${{ steps.meta.outputs.version }}
    steps:
      - uses: actions/checkout@v4

      - name: Generate image metadata
        id: meta
        run: |
          SHORT_SHA=${GITHUB_SHA::8}
          echo "version=dev-${SHORT_SHA}" >> $GITHUB_OUTPUT

      - name: Login to ACR
        uses: docker/login-action@v3
        with:
          registry: myacrcicd.azurecr.io
          username: ${{ secrets.ACR_USERNAME }}
          password: ${{ secrets.ACR_PASSWORD }}

      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          push: true
          tags: myacrcicd.azurecr.io/myapp:${{ steps.meta.outputs.version }}
          cache-from: type=registry,ref=myacrcicd.azurecr.io/myapp:cache
          cache-to: type=registry,ref=myacrcicd.azurecr.io/myapp:cache,mode=max
```

**验证**: merge 一个 PR 到 develop，去 ACR 界面看镜像是否出现。

---

### 第四阶段（Week 4）：自动部署到 DEV 环境

**目标**: 镜像推送后，自动拉取部署到 Azure VM

#### 4.1 创建 Azure VM

```bash
# B2s: 2 vCPU, 4GB RAM，足够跑 dev + test 两个容器
az vm create \
  --resource-group cicd-demo-rg \
  --name vm-dev-test \
  --image Ubuntu2204 \
  --size Standard_B2s \
  --admin-username azureuser \
  --generate-ssh-keys      # 私钥会显示，保存到 GitHub Secrets

# 开放端口
az vm open-port --resource-group cicd-demo-rg --name vm-dev-test --port 3000,3001

# SSH 进去安装 Docker
ssh azureuser@<公网IP>
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker azureuser
# 配置 ACR 登录（让 VM 能拉取镜像）
docker login myacrcicd.azurecr.io -u <username> -p <password>
```

#### 4.2 在 CI 流水线里加部署 Job

```yaml
# 接上面 ci.yml，继续加

deploy-dev:
  needs: build-and-push
  runs-on: ubuntu-latest
  environment: dev # GitHub Environment，可配置 Secrets

  steps:
    - name: Deploy to DEV
      uses: appleboy/ssh-action@v1
      with:
        host: ${{ secrets.DEV_HOST }} # VM 公网 IP
        username: azureuser
        key: ${{ secrets.DEV_SSH_KEY }} # 私钥
        script: |
          IMAGE=myacrcicd.azurecr.io/myapp:${{ needs.build-and-push.outputs.image-tag }}
          docker pull $IMAGE
          docker stop myapp-dev || true
          docker rm   myapp-dev || true
          docker run -d \
            --name myapp-dev \
            --env-file /home/azureuser/dev.env \
            -p 3000:3000 \
            --restart unless-stopped \
            $IMAGE

    - name: Smoke Test
      run: |
        sleep 5
        curl --fail http://${{ secrets.DEV_HOST }}:3000/health || exit 1
```

**验证**: push 代码到 develop，观察完整链路：单测 → 构建镜像 → 部署 → 健康检查。

---

### 第五阶段（Week 5）：Release 分支 + TEST 环境

**目标**: 体验完整的 RC 发布流程

```bash
# 从 develop 切出 release 分支
git checkout -b release/1.0.0 develop
git push origin release/1.0.0
```

```yaml
# .github/workflows/release-build.yml
name: Release Build - Deploy TEST

on:
  push:
    branches: ["release/**"]

jobs:
  build-rc:
    runs-on: ubuntu-latest
    outputs:
      rc-tag: ${{ steps.rc.outputs.tag }}
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Determine RC number
        id: rc
        run: |
          VERSION=${GITHUB_REF#refs/heads/release/}
          # 查询 ACR 中已有的 RC 数量，自动递增
          EXISTING=$(az acr repository show-tags \
            --name myacrcicd --repository myapp \
            --query "[?starts_with(@,'${VERSION}-rc')]" \
            -o tsv | wc -l)
          RC_NUM=$((EXISTING + 1))
          echo "tag=${VERSION}-rc.${RC_NUM}" >> $GITHUB_OUTPUT
        env:
          AZURE_CREDENTIALS: ${{ secrets.AZURE_CREDENTIALS }}

      - name: Build and push RC image
        uses: docker/build-push-action@v5
        with:
          push: true
          tags: myacrcicd.azurecr.io/myapp:${{ steps.rc.outputs.tag }}

  deploy-test:
    needs: build-rc
    runs-on: ubuntu-latest
    environment: test
    steps:
      - name: Deploy to TEST (same VM, different port)
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.DEV_HOST }} # 同一台 B2s VM
          username: azureuser
          key: ${{ secrets.DEV_SSH_KEY }}
          script: |
            IMAGE=myacrcicd.azurecr.io/myapp:${{ needs.build-rc.outputs.rc-tag }}
            docker pull $IMAGE
            docker stop myapp-test || true
            docker rm   myapp-test || true
            docker run -d \
              --name myapp-test \
              --env-file /home/azureuser/test.env \
              -p 3001:3000 \
              $IMAGE
```

**验证**: 在 release/1.0.0 分支上修一个 bug 再 push，观察自动生成 rc.2。

---

### 第六阶段（Week 6）：Tag 触发 + UAT + 人工审批

**目标**: 体验生产发布前的完整审批流

#### 6.1 创建 UAT VM

```bash
# B1s 够了，UAT 通常不需要高性能
az vm create \
  --resource-group cicd-demo-rg \
  --name vm-uat \
  --image Ubuntu2204 \
  --size Standard_B1s \
  --admin-username azureuser \
  --ssh-key-values ~/.ssh/id_rsa.pub

az vm open-port --resource-group cicd-demo-rg --name vm-uat --port 80
```

#### 6.2 配置 GitHub Environment 审批

```
GitHub → 你的仓库 → Settings → Environments

创建 "uat":
  - Protection rules: 无（自动部署）

创建 "production":
  - Required reviewers: 填你自己的 GitHub 账号
  - Wait timer: 0 minutes
  - Deployment branches: main only
```

#### 6.3 Release Deploy 流水线（tag 触发）

```yaml
# .github/workflows/release-deploy.yml
name: Release Deploy

on:
  push:
    tags: ["v*.*.*"]

jobs:
  promote-image:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.ver.outputs.version }}
    steps:
      - name: Extract version
        id: ver
        run: echo "version=${GITHUB_REF#refs/tags/v}" >> $GITHUB_OUTPUT

      - name: Login to ACR
        uses: docker/login-action@v3
        with:
          registry: myacrcicd.azurecr.io
          username: ${{ secrets.ACR_USERNAME }}
          password: ${{ secrets.ACR_PASSWORD }}

      - name: Promote RC image to release tag
        run: |
          VERSION=${{ steps.ver.outputs.version }}
          # 找到最新的 RC（不重新构建！）
          RC_TAG=$(az acr repository show-tags \
            --name myacrcicd --repository myapp \
            --orderby time_desc --query "[?starts_with(@,'${VERSION}-rc')]|[0]" \
            -o tsv)
          echo "Promoting $RC_TAG → $VERSION"
          docker pull myacrcicd.azurecr.io/myapp:$RC_TAG
          docker tag  myacrcicd.azurecr.io/myapp:$RC_TAG \
                      myacrcicd.azurecr.io/myapp:$VERSION
          docker push myacrcicd.azurecr.io/myapp:$VERSION
        env:
          AZURE_CREDENTIALS: ${{ secrets.AZURE_CREDENTIALS }}

  deploy-uat:
    needs: promote-image
    runs-on: ubuntu-latest
    environment: uat
    steps:
      - uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.UAT_HOST }}
          username: azureuser
          key: ${{ secrets.UAT_SSH_KEY }}
          script: |
            IMAGE=myacrcicd.azurecr.io/myapp:${{ needs.promote-image.outputs.version }}
            docker pull $IMAGE
            docker stop myapp-uat || true && docker rm myapp-uat || true
            docker run -d --name myapp-uat --env-file /home/azureuser/uat.env -p 80:3000 $IMAGE

  # 此 job 使用配置了 Required reviewers 的 environment，会自动暂停等待审批
  await-approval:
    needs: deploy-uat
    runs-on: ubuntu-latest
    environment: production # ← 触发审批弹窗
    steps:
      - run: echo "UAT verified. Approved for production."

  deploy-prod:
    needs: await-approval
    runs-on: ubuntu-latest
    steps:
      - run: echo "生产部署（本练习阶段可模拟输出即可）"
```

**验证**:

```bash
git tag v1.0.0 && git push origin v1.0.0
```

观察流水线暂停在 `await-approval`，去 GitHub Actions 页面点击 Approve，然后继续执行。

---

### 第七阶段（Week 7）：回滚 + 通知

**目标**: 故障时能快速回滚，部署结果有通知

#### 健康检查失败自动回滚

```yaml
# 在 deploy-dev job 的 ssh script 里
script: |
  IMAGE=myacrcicd.azurecr.io/myapp:${{ needs.build-and-push.outputs.image-tag }}
  PREV_IMAGE=$(docker inspect myapp-dev --format='{{.Config.Image}}' 2>/dev/null || echo "")

  docker pull $IMAGE
  docker stop myapp-dev || true
  docker rm   myapp-dev || true
  docker run -d --name myapp-dev -p 3000:3000 $IMAGE

  # 等待启动，做健康检查
  sleep 8
  if ! curl -sf http://localhost:3000/health; then
    echo "Health check failed! Rolling back..."
    docker stop myapp-dev
    docker rm   myapp-dev
    # 回滚到上一个版本
    if [ -n "$PREV_IMAGE" ]; then
      docker run -d --name myapp-dev -p 3000:3000 $PREV_IMAGE
    fi
    exit 1
  fi
```

#### 企微/钉钉 Webhook 通知

```yaml
# 在流水线最后加
notify:
  needs: [deploy-dev]
  runs-on: ubuntu-latest
  if: always() # 无论成功失败都通知
  steps:
    - name: Notify WeChat Work
      run: |
        STATUS="${{ needs.deploy-dev.result }}"
        COLOR=$([ "$STATUS" = "success" ] && echo "green" || echo "red")
        curl -X POST ${{ secrets.WECHAT_WEBHOOK }} \
          -H 'Content-Type: application/json' \
          -d "{
            \"msgtype\": \"markdown\",
            \"markdown\": {
              \"content\": \"### CI/CD 通知\n
              > **状态**: <font color='${COLOR}'>${STATUS}</font>\n
              > **分支**: ${{ github.ref_name }}\n
              > **提交**: ${{ github.sha }}\n
              > **触发人**: ${{ github.actor }}\"
            }
          }"
```

---

### 第八阶段（Week 8）：完整演练 + 进阶

**目标**: 模拟真实工作流，走一遍完整的从需求到上线

```
完整流程演练顺序:

1. git checkout -b feature/add-user-api develop
   → 写代码 → push → 创建 PR
   → 观察 PR Validation 流水线运行
   → 故意写低覆盖率代码 → 观察 SonarCloud 拦截

2. 修好代码 → PR 被批准 → merge to develop
   → 观察 CI 流水线: 测试 → 构建 → 部署 DEV

3. git checkout -b release/1.1.0 develop
   → git push origin release/1.1.0
   → 观察 RC 镜像构建 → 部署 TEST

4. 模拟 QA 发现 bug → 在 release/1.1.0 上修复 → push
   → 观察自动生成 rc.2

5. QA 验证通过
   → PR: release/1.1.0 → main → merge
   → git tag v1.1.0 && git push origin v1.1.0
   → 观察: 镜像晋级 → UAT 部署 → 审批弹窗 → 点击审批 → 生产部署
```

---

## 四、进阶方向（预算用完前可探索）

```
方向                  Azure 服务               学习价值
─────────────────────────────────────────────────────────
容器编排              Azure Container Apps     无需管理 K8s，自动伸缩
Secret 管理           Azure Key Vault          生产级密钥管理，替代 GitHub Secrets
基础设施即代码         Terraform + Azure        一键重建整个环境
监控 & 告警           Azure Monitor + Grafana  观察部署后的应用状态
蓝绿部署              Azure Container Apps     零停机发布
```

---

## 五、每周检查清单

```
Week 1  □ 第一条 Actions 流水线跑通单测
Week 2  □ SonarCloud Quality Gate 成功拦截过一次 PR
Week 3  □ ACR 里能看到推送的镜像
Week 4  □ push 到 develop 后 DEV 服务器自动更新
Week 5  □ release 分支自动生成 rc.1, rc.2
Week 6  □ tag 触发 + 人工审批流程走通
Week 7  □ 故意让健康检查失败，验证自动回滚
Week 8  □ 完整演练一遍，全程无手动操作
```

8 周后你将拥有一套可以写进简历的完整 CI/CD 实践经历。
