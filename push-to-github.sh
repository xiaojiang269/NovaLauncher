#!/usr/bin/env bash
# NovaLauncher 一键开源推送脚本
# 用法：先在终端设置你的 GitHub PAT（repo 权限），再运行本脚本：
#   export GITHUB_TOKEN=ghp_你的令牌
#   bash push-to-github.sh
set -e
cd "$(dirname "$0")"

echo "=== NovaLauncher 一键开源推送 ==="
echo "目标：创建公开仓库 xiaojiang269/NovaLauncher 并推送全部源码"
echo ""

if [ -z "${GITHUB_TOKEN}" ]; then
  echo "错误：未设置 GITHUB_TOKEN。"
  echo "请先运行：export GITHUB_TOKEN=ghp_你的令牌（repo 权限 PAT）"
  exit 1
fi

BRANCH="$(git branch --show-current)"

# 1) 创建公开仓库（若不存在）
CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer ${GITHUB_TOKEN}" \
  https://api.github.com/repos/xiaojiang269/NovaLauncher)
if [ "$CODE" = "404" ]; then
  echo "[1/2] 创建公开仓库 xiaojiang269/NovaLauncher ..."
  curl -s -X POST -H "Authorization: Bearer ${GITHUB_TOKEN}" \
    -H "Accept: application/vnd.github+json" \
    https://api.github.com/user/repos \
    -d '{"name":"NovaLauncher","public":true,"description":"跨平台 Minecraft 第三方启动器（C#/Avalonia）：全版本下载 / 模组整合包 / 联机 / 外置登录 / 海外源"}'
  echo ""
else
  echo "[1/2] 仓库已存在，跳过创建"
fi

# 2) 推送
echo "[2/2] 推送 ${BRANCH} 分支 ..."
git remote remove origin 2>/dev/null || true
git remote add origin "https://xiaojiang269:${GITHUB_TOKEN}@github.com/xiaojiang269/NovaLauncher.git"
git push -u origin "${BRANCH}"

echo ""
echo "=== 完成 ==="
echo "仓库地址：https://github.com/xiaojiang269/NovaLauncher"
echo "请勿泄露你的令牌（已用于本次推送）。"
