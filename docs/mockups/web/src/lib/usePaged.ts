import { useCallback, useEffect, useState } from "react";
import { ApiError, NetworkError, type Page } from "./api";

export type PagedStatus = "loading" | "ready" | "error" | "denied";

/** Paginación por cursor simulada: acumula páginas y expone los estados transversales. */
export function usePaged<T>(load: (cursor: string | null) => Promise<Page<T>>) {
  const [rows, setRows] = useState<T[]>([]);
  const [total, setTotal] = useState(0);
  const [cursor, setCursor] = useState<string | null>(null);
  const [status, setStatus] = useState<PagedStatus>("loading");
  const [loadingMore, setLoadingMore] = useState(false);
  const [message, setMessage] = useState("");

  const fetchPage = useCallback(
    async (from: string | null, append: boolean) => {
      if (append) setLoadingMore(true);
      else setStatus("loading");
      try {
        const data = await load(from);
        setRows((previous) => (append ? [...previous, ...data.page] : data.page));
        setTotal(data.total);
        setCursor(data.nextCursor);
        setStatus("ready");
      } catch (error) {
        if (error instanceof ApiError && error.status === 403) {
          setMessage(error.message);
          setStatus("denied");
          return;
        }
        setMessage(error instanceof NetworkError ? error.message : "No pudimos traer la información.");
        setStatus("error");
      } finally {
        setLoadingMore(false);
      }
    },
    [load],
  );

  useEffect(() => {
    void fetchPage(null, false);
  }, [fetchPage]);

  return {
    rows,
    total,
    status,
    message,
    loadingMore,
    hasMore: cursor !== null,
    more: () => void fetchPage(cursor, true),
    reload: () => void fetchPage(null, false),
  };
}
