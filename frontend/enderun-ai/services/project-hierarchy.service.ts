import { apiClient } from "@/lib/api/api-client";

export enum ProjectModuleType {
  Hakedis = 0,
  Personnel = 1,
  Warehouse = 2,
  Purchasing = 3,
  Finance = 4,
}

export type ProjectHierarchyLevel = {
  id: string;
  code: string;
  name: string;
  sortOrder: number;
  isRequired: boolean;
  nodeCount: number;
};

export type ProjectModuleScopeCount = {
  moduleType: ProjectModuleType;
  count: number;
};

export type ProjectHierarchyNode = {
  id: string;
  levelId: string;
  levelName: string;
  levelSortOrder: number;
  parentNodeId?: string | null;
  code: string;
  name: string;
  description?: string | null;
  sortOrder: number;
  path: string;
  moduleScopes: ProjectModuleScopeCount[];
  children: ProjectHierarchyNode[];
};

export type ProjectHierarchyTree = {
  projectId: string;
  projectCode: string;
  projectName: string;
  levels: ProjectHierarchyLevel[];
  nodes: ProjectHierarchyNode[];
};

export type CreateHierarchyLevelRequest = {
  code: string;
  name: string;
  sortOrder: number;
  isRequired: boolean;
};

export type CreateHierarchyNodeRequest = {
  levelId: string;
  parentNodeId?: string | null;
  code: string;
  name: string;
  description?: string | null;
  sortOrder: number;
};

export const projectHierarchyService = {
  getTree(projectId: string) {
    return apiClient<ProjectHierarchyTree>(
      `projects/${projectId}/hierarchy`
    );
  },

  createLevel(
    projectId: string,
    payload: CreateHierarchyLevelRequest
  ) {
    return apiClient<ProjectHierarchyLevel>(
      `projects/${projectId}/hierarchy/levels`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  deleteLevel(projectId: string, levelId: string) {
    return apiClient<boolean>(
      `projects/${projectId}/hierarchy/levels/${levelId}`,
      { method: "DELETE" }
    );
  },

  createNode(
    projectId: string,
    payload: CreateHierarchyNodeRequest
  ) {
    return apiClient<ProjectHierarchyNode>(
      `projects/${projectId}/hierarchy/nodes`,
      {
        method: "POST",
        body: payload,
      }
    );
  },

  deleteNode(projectId: string, nodeId: string) {
    return apiClient<boolean>(
      `projects/${projectId}/hierarchy/nodes/${nodeId}`,
      { method: "DELETE" }
    );
  },

  applyMkeTemplate(projectId: string) {
    return apiClient<{
      createdLevelCount: number;
      createdNodeCount: number;
      hierarchy: ProjectHierarchyTree;
    }>(`projects/${projectId}/hierarchy/templates/mke`, {
      method: "POST",
    });
  },
};
