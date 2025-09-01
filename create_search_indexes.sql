-- LIKE 검색 성능 향상을 위한 인덱스 생성
-- 기본 B-tree 인덱스
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_site_bbs_info_title 
ON tmtmfhgi.site_bbs_info USING btree (title);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_site_bbs_info_content 
ON tmtmfhgi.site_bbs_info USING btree (content);

-- 복합 검색을 위한 인덱스
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_site_bbs_info_title_content 
ON tmtmfhgi.site_bbs_info USING btree (title, content);

-- 사이트별 검색을 위한 복합 인덱스
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_site_bbs_info_site_title 
ON tmtmfhgi.site_bbs_info USING btree (site, title);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_site_bbs_info_site_content 
ON tmtmfhgi.site_bbs_info USING btree (site, content);

-- PostgreSQL LIKE 패턴 매칭 최적화를 위한 text_pattern_ops 인덱스
-- 이 인덱스는 'text LIKE 'pattern%'' 형태의 검색에 최적화됨
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_site_bbs_info_title_pattern 
ON tmtmfhgi.site_bbs_info USING btree (title text_pattern_ops);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_site_bbs_info_content_pattern 
ON tmtmfhgi.site_bbs_info USING btree (content text_pattern_ops);

-- 인덱스 생성 확인
SELECT 
    schemaname,
    tablename,
    indexname,
    indexdef
FROM pg_indexes 
WHERE tablename = 'site_bbs_info' AND schemaname = 'tmtmfhgi'
ORDER BY indexname;