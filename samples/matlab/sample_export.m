%% settings
scheme          = 'https';
host            = 'localhost';
port         	= 5001;
username        = 'test@nexus.org';
password        = '#test0/User1'; % password = input('Please enter your password: ')
targetFolder    = 'data';

dateTimeBegin 	= datetime(2020, 02, 01, 0, 0, 0, 'TimeZone', 'UTC');
dateTimeEnd 	= datetime(2020, 02, 02, 0, 0, 0, 'TimeZone', 'UTC');

% must all be of the same sample rate
resourcePaths = { ...
    '/IN_MEMORY/TEST/ACCESSIBLE/T1/1 s_mean'
    '/IN_MEMORY/TEST/ACCESSIBLE/V1/1 s_mean'
};

%% load connector script
connectorFolderPath = fullfile(tempdir, 'Nexus');
[~,~ ]              = mkdir(connectorFolderPath);
url                 = sprintf('%s://%s:%d/connectors/NexusConnector.m', scheme, host, port);
websave(fullfile(connectorFolderPath, 'NexusConnector.m'), url);
addpath(connectorFolderPath)

%% export data
connector = NexusConnector(scheme, host, port, username, password);
% without authentication: connector = NexusConnector(scheme, host, port)

params.FileGranularity                      = 'Day';    % Minute_1, Minute_10, Hour, Day, SingleFile
params.FileFormat                           = 'MAT73';  % CSV, FAMOS, MAT73
params.ResourcePaths                         = resourcePaths;

% CSV-only: 
params.Configuration.SignificantFigures     = '4';
params.Configuration.RowIndexFormat         = 'Index'   % Index, Unix, Excel

connector.Export(dateTimeBegin, dateTimeEnd, params, targetFolder);