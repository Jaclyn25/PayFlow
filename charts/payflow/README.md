# PayFlow Helm Chart

Production-ready Helm chart for deploying the PayFlow financial platform on Kubernetes (EKS, GKE, AKS, Minikube).

## Prerequisites

- Kubernetes 1.26+
- Helm 3.8+
- Ingress controller (optional, e.g., ingress-nginx)
- cert-manager (optional, for TLS certificates)

## Installation

### 1. Install Chart with Default Values
```bash
helm install payflow ./charts/payflow --namespace payflow --create-namespace
```

### 2. Install Chart with Custom Values
```bash
helm install payflow ./charts/payflow \
  --namespace payflow \
  --create-namespace \
  -f custom-values.yaml
```

### 3. Upgrade Existing Release
```bash
helm upgrade payflow ./charts/payflow --namespace payflow
```

### 4. Uninstall Release
```bash
helm uninstall payflow --namespace payflow
```

## Configuration Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `replicaCount` | Number of pod replicas | `2` |
| `image.repository` | Docker image repository | `ghcr.io/jaclyn25/payflow-api` |
| `image.tag` | Docker image tag | `1.0.0` |
| `image.pullPolicy` | Image pull policy | `IfNotPresent` |
| `service.type` | Kubernetes service type | `ClusterIP` |
| `service.port` | Service listening port | `80` |
| `service.targetPort` | Container target port | `8080` |
| `ingress.enabled` | Enable ingress controller | `true` |
| `ingress.className` | Ingress class name | `nginx` |
| `autoscaling.enabled` | Enable Horizontal Pod Autoscaler | `true` |
| `autoscaling.minReplicas` | Minimum pod replicas | `2` |
| `autoscaling.maxReplicas` | Maximum pod replicas | `10` |
| `autoscaling.targetCPUUtilizationPercentage` | Target CPU % for scaling | `70` |
| `autoscaling.targetMemoryUtilizationPercentage` | Target Memory % for scaling | `80` |
| `probes.startup.path` | Startup probe path | `/liveness` |
| `probes.liveness.path` | Liveness probe path | `/liveness` |
| `probes.readiness.path` | Readiness probe path | `/readiness` |
