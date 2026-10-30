SET QUOTED_IDENTIFIER ON;
SELECT i.original_text + '|' + CAST(r.rank AS varchar(4)) + '|' + r.code + '|' + r.title
FROM coding_diagnosis_input i
JOIN coding_recommendation r ON r.diagnosis_input_id = i.id
WHERE i.hospital_id = 'ff840089-ea7d-4c40-bb4c-5d47b4b49956'
  AND r.lifecycle_status = 'ACTIVE'
  AND i.source_type IN ('DOCUMENT_AUTO', 'FRONT_PAGE')
ORDER BY i.original_text, r.rank;