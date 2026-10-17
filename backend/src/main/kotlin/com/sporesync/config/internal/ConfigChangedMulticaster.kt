package com.sporesync.config.internal

import com.sporesync.config.ConfigChanged
import org.slf4j.LoggerFactory
import org.springframework.beans.factory.BeanFactory
import org.springframework.context.ApplicationEvent
import org.springframework.context.ApplicationListener
import org.springframework.context.PayloadApplicationEvent
import org.springframework.context.event.SimpleApplicationEventMulticaster
import org.springframework.context.support.AbstractApplicationContext
import org.springframework.stereotype.Component

/**
 * Spring stops delivering an event at the first synchronous listener that throws. A [ConfigChanged] is published
 * after the save is committed, so one failing listener must not leave the rest on stale settings. Other events keep
 * Spring's default behavior.
 */
@Component(AbstractApplicationContext.APPLICATION_EVENT_MULTICASTER_BEAN_NAME)
internal class ConfigChangedMulticaster(
    beanFactory: BeanFactory,
) : SimpleApplicationEventMulticaster(beanFactory) {
    private val log = LoggerFactory.getLogger(ConfigChangedMulticaster::class.java)

    @Suppress("TooGenericExceptionCaught")
    override fun invokeListener(
        listener: ApplicationListener<*>,
        event: ApplicationEvent,
    ) {
        if ((event as? PayloadApplicationEvent<*>)?.payload !is ConfigChanged) {
            return super.invokeListener(
                listener,
                event,
            )
        }
        try {
            super.invokeListener(listener, event)
        } catch (e: RuntimeException) {
            log.error("A ConfigChanged listener failed; the remaining listeners still receive the change", e)
        }
    }
}
