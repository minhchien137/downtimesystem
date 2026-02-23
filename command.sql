SELECT * FROM [svn_pentaho].[dbo].[SVN_Downtime_Info]

DELETE FROM dbo.SVN_Downtime_Info;
DBCC CHECKIDENT ('dbo.SVN_Downtime_Info', RESEED, 0);

Select * from dbo.SVN_Downtime_Reason

