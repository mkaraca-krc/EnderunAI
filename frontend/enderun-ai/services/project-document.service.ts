import { apiClient } from "@/lib/api/api-client";

export const SUGGESTED_DOCUMENT_FOLDERS = [
  "Keşif",
  "Çizimler",
  "Şartnameler",
  "Sözleşme",
  "Saha Fotoğrafları",
  "Hakediş Ekleri",
  "Diğer",
];

export type ProjectDocumentListItem = {
  id: string;
  projectSiteId?: string | null;
  siteName?: string | null;
  folder: string;
  fileName: string;
  sizeBytes: number;
  contentType: string;
  extension: string;
  uploadedByUserId: string;
  uploadedByName: string;
  description?: string | null;
  versionNumber: number;
  createdAtUtc: string;
};

export type ProjectDocumentVersion = {
  id: string;
  versionNumber: number;
  isCurrentVersion: boolean;
  sizeBytes: number;
  uploadedByUserId: string;
  uploadedByName: string;
  createdAtUtc: string;
  description?: string | null;
};

export type UploadProjectDocumentsResult = {
  message: string;
  files: { fileName: string; versionNumber: number }[];
};

function root(projectId: string) {
  return `projects/${projectId}/documents`;
}

export const projectDocumentService = {
  getAll(
    projectId: string,
    filters: { siteId?: string; folder?: string; search?: string } = {}
  ) {
    const params = new URLSearchParams();
    if (filters.siteId) params.set("siteId", filters.siteId);
    if (filters.folder) params.set("folder", filters.folder);
    if (filters.search) params.set("search", filters.search);
    const query = params.toString();

    return apiClient<ProjectDocumentListItem[]>(
      `${root(projectId)}${query ? `?${query}` : ""}`
    );
  },

  getFolders(projectId: string) {
    return apiClient<string[]>(`${root(projectId)}/folders`);
  },

  getVersions(projectId: string, documentId: string) {
    return apiClient<ProjectDocumentVersion[]>(
      `${root(projectId)}/${documentId}/versions`
    );
  },

  downloadUrl(projectId: string, documentId: string) {
    return `/api/backend/${root(projectId)}/${documentId}/download`;
  },

  updateDescription(projectId: string, documentId: string, description: string) {
    return apiClient<{ message: string }>(`${root(projectId)}/${documentId}`, {
      method: "PATCH",
      body: { description },
    });
  },

  delete(projectId: string, documentId: string) {
    return apiClient<{ message: string }>(`${root(projectId)}/${documentId}`, {
      method: "DELETE",
    });
  },

  upload(
    projectId: string,
    files: File[],
    folder: string,
    options: {
      projectSiteId?: string;
      description?: string;
      onProgress?: (percent: number) => void;
    } = {}
  ): Promise<UploadProjectDocumentsResult> {
    return new Promise((resolve, reject) => {
      const formData = new FormData();
      files.forEach((file) => formData.append("Files", file));
      formData.append("Folder", folder);
      if (options.projectSiteId) {
        formData.append("ProjectSiteId", options.projectSiteId);
      }
      if (options.description) {
        formData.append("Description", options.description);
      }

      const xhr = new XMLHttpRequest();
      xhr.open("POST", `/api/backend/${root(projectId)}`);
      xhr.withCredentials = true;

      xhr.upload.onprogress = (event) => {
        if (event.lengthComputable && options.onProgress) {
          options.onProgress(Math.round((event.loaded / event.total) * 100));
        }
      };

      xhr.onload = () => {
        let payload: unknown = null;
        try {
          payload = JSON.parse(xhr.responseText);
        } catch {
          // yanıt JSON değilse yoksay
        }

        if (xhr.status >= 200 && xhr.status < 300) {
          resolve(payload as UploadProjectDocumentsResult);
          return;
        }

        if (xhr.status === 401 && typeof window !== "undefined") {
          window.location.href = "/login";
        }

        const message =
          (payload as { message?: string } | null)?.message ??
          `Yükleme başarısız: ${xhr.status}`;
        reject(new Error(message));
      };

      xhr.onerror = () => reject(new Error("Sunucuya bağlanılamadı."));
      xhr.send(formData);
    });
  },
};

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toLocaleString("tr-TR", {
      maximumFractionDigits: 1,
    })} KB`;
  }
  return `${(bytes / (1024 * 1024)).toLocaleString("tr-TR", {
    maximumFractionDigits: 1,
  })} MB`;
}
