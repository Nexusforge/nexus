using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataWriter
    {
        #region Properties

        ILogger Logger { set; }

        IConfiguration Configuration { set; }

        #endregion

#error TODO 1: Repair plugins (IConfiguration)
#error TODO 2: Reimplement data writers, i.e. no more 1 minute limit
#error TODO 3: Make this interface async

        #region Methods

        Task OnParametersSetAsync()
        {
            return Task.CompletedTask;
        }

        void OnPrepareFile(DateTime startDateTime, List<ChannelContextGroup> channelContextGroupSet);

        void OnWrite(ChannelContextGroup contextGroup, ulong fileOffset, ulong bufferOffset, ulong length);

        #endregion
    }
}