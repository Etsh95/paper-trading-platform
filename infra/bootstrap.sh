#!/usr/bin/env bash
# One-time setup of Azure + GitHub for CI/CD. Safe to re-run.
#
#   REPO=<owner>/<repo> ./infra/bootstrap.sh
#
# Regions: Azure sometimes stops accepting new (especially free-trial) customers in busy regions. Run
#   az deployment group validate ... -p location=<region>
# to check a region before deploying, and override API_LOCATION / WEB_LOCATION as needed.
#
# Prerequisites: az (logged in to the target subscription), gh (logged in), docker with buildx.
#
# What it does:
#   1. Creates the resource group and base infrastructure (registry, logs, Container Apps environment, Static Web App).
#   2. Builds the first API image and pushes it to the registry.
#   3. Creates the API container app from that image.
#   4. Creates a GitHub deployment identity in Entra ID with a *federated credential* (OIDC): GitHub Actions
#      exchanges a short-lived token for Azure access, so no Azure password or key is stored in GitHub.
#      Its roles are scoped to the individual resources it deploys, not the subscription.
#   5. Writes the (non-secret) resource identifiers to GitHub repository variables.
set -euo pipefail

: "${REPO:?Set REPO=<owner>/<repo>}"
RG="${RG:-rg-papertrading}"
LOCATION="${LOCATION:-westeurope}"          # resource group metadata only
API_LOCATION="${API_LOCATION:-northeurope}"  # outside the US (Binance geo-blocks US ranges)
WEB_LOCATION="${WEB_LOCATION:-eastasia}"     # Static Web App metadata; the site itself is served globally
PREFIX="${PREFIX:-papertrading}"
GH_ENVIRONMENT="${GH_ENVIRONMENT:-production}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

step() { printf '\n==> %s\n' "$*"; }

step "Azure subscription: $(az account show --query name -o tsv)"
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)

step "Resource group $RG ($LOCATION)"
az group create -n "$RG" -l "$LOCATION" -o none

output() { az deployment group show -g "$RG" -n main --query "properties.outputs.$1.value" -o tsv; }

step "Base infrastructure (pass 1: without the API)"
# Keep the API if it already exists (re-run), otherwise create everything else first.
if az resource show -g "$RG" -n "$PREFIX-api" --resource-type Microsoft.App/containerApps -o none 2>/dev/null; then
  CURRENT_IMAGE=$(az resource show -g "$RG" -n "$PREFIX-api" --resource-type Microsoft.App/containerApps --query "properties.template.containers[0].image" -o tsv)
  az deployment group create -g "$RG" -n main -f "$ROOT/infra/main.bicep" -p prefix="$PREFIX" location="$API_LOCATION" webLocation="$WEB_LOCATION" apiImage="$CURRENT_IMAGE" -o none
else
  az deployment group create -g "$RG" -n main -f "$ROOT/infra/main.bicep" -p prefix="$PREFIX" location="$API_LOCATION" webLocation="$WEB_LOCATION" deployApi=false -o none

  REGISTRY=$(output registryName)
  LOGIN_SERVER=$(output registryLoginServer)
  TAG="bootstrap-$(git -C "$ROOT" rev-parse --short HEAD)"

  step "First API image -> $LOGIN_SERVER/papertrading-api:$TAG"
  az acr login -n "$REGISTRY"
  docker buildx build --platform linux/amd64 -t "$LOGIN_SERVER/papertrading-api:$TAG" --push "$ROOT/backend"

  step "API container app (pass 2)"
  az deployment group create -g "$RG" -n main -f "$ROOT/infra/main.bicep" \
    -p prefix="$PREFIX" location="$API_LOCATION" webLocation="$WEB_LOCATION" apiImage="$LOGIN_SERVER/papertrading-api:$TAG" -o none
fi

REGISTRY=$(output registryName)
LOGIN_SERVER=$(output registryLoginServer)
API_NAME=$(output apiName)
API_URL=$(output apiUrl)
WEB_NAME=$(output webName)
WEB_URL=$(output webUrl)

step "GitHub deployment identity (OIDC, no stored secrets)"
APP_NAME="$PREFIX-github-deploy"
APP_ID=$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv)
if [[ -z "$APP_ID" ]]; then
  APP_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
fi
az ad sp show --id "$APP_ID" -o none 2>/dev/null || az ad sp create --id "$APP_ID" -o none
SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)

# GitHub's OIDC subject comes in two shapes: the classic "repo:owner/name:..." and the newer format that pins the
# immutable owner and repository IDs ("repo:owner@123/name@456:..."), which survives renames and can't be
# claimed by a re-created repo of the same name. Trust both, but only for this repo's production environment.
OWNER_ID=$(gh api "repos/$REPO" -q .owner.id)
REPO_ID=$(gh api "repos/$REPO" -q .id)
OWNER=${REPO%%/*}
NAME=${REPO#*/}
add_subject() {
  local name="$1" subject="$2"
  if [[ -z "$(az ad app federated-credential list --id "$APP_ID" --query "[?subject=='$subject'].name" -o tsv)" ]]; then
    az ad app federated-credential create --id "$APP_ID" -o none --parameters "{
      \"name\": \"$name\",
      \"issuer\": \"https://token.actions.githubusercontent.com\",
      \"subject\": \"$subject\",
      \"audiences\": [\"api://AzureADTokenExchange\"]
    }"
  fi
}
add_subject "github-$GH_ENVIRONMENT" "repo:$REPO:environment:$GH_ENVIRONMENT"
add_subject "github-$GH_ENVIRONMENT-ids" "repo:$OWNER@$OWNER_ID/$NAME@$REPO_ID:environment:$GH_ENVIRONMENT"

assign() {
  local role="$1" scope="$2"
  if [[ -z "$(az role assignment list --assignee "$SP_OBJECT_ID" --role "$role" --scope "$scope" --query "[].id" -o tsv)" ]]; then
    az role assignment create --assignee-object-id "$SP_OBJECT_ID" --assignee-principal-type ServicePrincipal \
      --role "$role" --scope "$scope" -o none
  fi
}
# Least privilege: push images to this registry, update this container app, read this static site's deploy token.
assign AcrPush "$(az resource show -g "$RG" -n "$REGISTRY" --resource-type Microsoft.ContainerRegistry/registries --query id -o tsv)"
assign Contributor "$(az resource show -g "$RG" -n "$API_NAME" --resource-type Microsoft.App/containerApps --query id -o tsv)"
assign Contributor "$(az resource show -g "$RG" -n "$WEB_NAME" --resource-type Microsoft.Web/staticSites --query id -o tsv)"

step "GitHub environment '$GH_ENVIRONMENT' and repository variables"
gh api -X PUT "repos/$REPO/environments/$GH_ENVIRONMENT" --silent
set_var() { gh variable set "$1" --body "$2" -R "$REPO"; }
set_var AZURE_CLIENT_ID "$APP_ID"
set_var AZURE_TENANT_ID "$TENANT_ID"
set_var AZURE_SUBSCRIPTION_ID "$SUBSCRIPTION_ID"
set_var AZURE_RESOURCE_GROUP "$RG"
set_var ACR_NAME "$REGISTRY"
set_var ACR_LOGIN_SERVER "$LOGIN_SERVER"
set_var API_APP_NAME "$API_NAME"
set_var API_URL "$API_URL"
set_var WEB_APP_NAME "$WEB_NAME"
set_var WEB_URL "$WEB_URL"

step "Done"
echo "API:      $API_URL"
echo "Frontend: $WEB_URL (populated by the first GitHub Actions run on main)"
