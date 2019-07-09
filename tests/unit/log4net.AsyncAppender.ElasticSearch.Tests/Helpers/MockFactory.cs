﻿using log4net.AsyncAppender;
using log4net.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Tests
{
    internal static class MockFactory
    {
        public static TestableElasticSearchAsyncAppender GetAnAppender(bool autoConfigure = true)
        {
            var (appender, _) = GetAnAppenderWithErrorHandler(autoConfigure);
            return appender;
        }

        public static (TestableElasticSearchAsyncAppender, MockErrorHandler) GetAnAppenderWithErrorHandler(bool autoConfigure = true)
        {
            var appender = new TestableElasticSearchAsyncAppender(autoConfigure);

            var mockErrorHandler = new MockErrorHandler();
            appender.ErrorHandler = mockErrorHandler;

            return (appender, mockErrorHandler);
        }
    }
}
