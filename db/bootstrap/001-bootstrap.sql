CREATE ROLE course_owner NOLOGIN;

CREATE ROLE course_migrator LOGIN PASSWORD 'course-migrator-dev';

CREATE ROLE course_publisher LOGIN PASSWORD 'course-publisher-dev';

CREATE ROLE course_runtime LOGIN PASSWORD 'course-runtime-dev';